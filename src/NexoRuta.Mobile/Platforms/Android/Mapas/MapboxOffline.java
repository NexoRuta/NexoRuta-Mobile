package uy.nexoruta.offline;

import android.os.Looper;
import com.mapbox.common.Cancelable;
import com.mapbox.common.MapboxOptions;
import com.mapbox.common.NetworkRestriction;
import com.mapbox.common.TileRegionLoadOptions;
import com.mapbox.common.TileRegionErrorType;
import com.mapbox.common.TileStore;
import com.mapbox.geojson.Point;
import com.mapbox.geojson.Polygon;
import com.mapbox.maps.GlyphsRasterizationMode;
import com.mapbox.maps.OfflineManager;
import com.mapbox.maps.StylePackLoadOptions;
import com.mapbox.maps.TilesetDescriptorOptions;
import org.json.JSONObject;
import java.util.Arrays;
import java.util.Collections;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;

/** Puente compilado contra los SDK incluidos por Indiko 1.8.0; no registra tokens ni direcciones. */
public final class MapboxOffline {
    private MapboxOffline() { }
    private static final ConcurrentHashMap<String, Job> jobs = new ConcurrentHashMap<>();
    private static TileStore store;
    private static OfflineManager manager;

    public static synchronized void configure(String token) {
        if (token == null || !token.startsWith("pk.")) throw new IllegalArgumentException("Public token required");
        MapboxOptions.setAccessToken(token);
        if (store == null) store = TileStore.create();
        if (manager == null) manager = new OfflineManager();
    }
    private static void ready() {
        if (store == null || manager == null) throw new IllegalStateException("Offline maps not configured");
    }
    public static void start(String id, String style, double minLat, double maxLat,
                             double minLon, double maxLon, int minZoom, int maxZoom) {
        ready();
        Job job = new Job();
        if (jobs.putIfAbsent(id, job) != null) throw new IllegalStateException("Region already downloading");
        try {
            manager.getStylePack(style, existing -> {
                if (!job.running()) return;
                if (existing.isValue() && existing.getValue().getRequiredResourceCount() > 0
                    && existing.getValue().getCompletedResourceCount() >= existing.getValue().getRequiredResourceCount()) {
                    job.style(existing.getValue().getCompletedResourceCount(), existing.getValue().getRequiredResourceCount(),
                        existing.getValue().getCompletedResourceSize(), true);
                    return;
                }
                Cancelable styleTask = manager.loadStylePack(style, new StylePackLoadOptions.Builder()
                    .glyphsRasterizationMode(GlyphsRasterizationMode.IDEOGRAPHS_RASTERIZED_LOCALLY).build(),
                    progress -> job.style(progress.getCompletedResourceCount(), progress.getRequiredResourceCount(), progress.getCompletedResourceSize(), false),
                    result -> {
                        if (result.isValue()) job.style(result.getValue().getCompletedResourceCount(),
                            result.getValue().getRequiredResourceCount(), result.getValue().getCompletedResourceSize(), true);
                        else job.fail();
                    });
                job.setStyleTask(styleTask);
            });
            if (!job.running()) return;
            TileRegionLoadOptions options = new TileRegionLoadOptions.Builder()
                .geometry(Polygon.fromLngLats(Collections.singletonList(Arrays.asList(
                    Point.fromLngLat(minLon, minLat), Point.fromLngLat(maxLon, minLat),
                    Point.fromLngLat(maxLon, maxLat), Point.fromLngLat(minLon, maxLat), Point.fromLngLat(minLon, minLat)))))
                .descriptors(Collections.singletonList(manager.createTilesetDescriptor(new TilesetDescriptorOptions.Builder()
                    .styleURI(style).minZoom((byte) minZoom).maxZoom((byte) maxZoom).build())))
                .acceptExpired(true).networkRestriction(NetworkRestriction.NONE).build();
            Cancelable tileTask = store.loadTileRegion(id, options,
                progress -> job.tiles(progress.getCompletedResourceCount(), progress.getRequiredResourceCount(), progress.getCompletedResourceSize(), false),
                result -> {
                    if (result.isValue()) job.tiles(result.getValue().getCompletedResourceCount(),
                        result.getValue().getRequiredResourceCount(), result.getValue().getCompletedResourceSize(), true);
                    else job.fail();
                });
            job.setTileTask(tileTask);
        } catch (RuntimeException error) { job.fail(); throw error; }
    }
    public static String progress(String id) {
        Job job = jobs.get(id);
        return job == null ? status("missing", 0, 0, false) : job.json();
    }
    public static void cancel(String id) {
        Job job = jobs.get(id);
        if (job != null) job.cancel();
    }
    public static void forget(String id) { jobs.remove(id); }
    public static void remove(String id) throws InterruptedException {
        ready();
        if (Looper.myLooper() == Looper.getMainLooper()) throw new IllegalStateException("Removal must run off the UI thread");
        cancel(id); jobs.remove(id);
        CountDownLatch finished = new CountDownLatch(1); final boolean[] removed = new boolean[1];
        store.removeTileRegion(id, result -> {
            removed[0] = result.isValue() || result.getError().getType() == TileRegionErrorType.DOES_NOT_EXIST;
            finished.countDown();
        });
        if (!finished.await(10, TimeUnit.SECONDS) || !removed[0]) throw new IllegalStateException("Tile region removal unconfirmed");
        // Los estilos pueden estar compartidos con otra hoja válida: nunca se eliminan al cancelar una región.
    }
    public static String inspect(String id, String style) throws InterruptedException {
        ready();
        if (Looper.myLooper() == Looper.getMainLooper()) throw new IllegalStateException("Query must run off the UI thread");
        CountDownLatch finished = new CountDownLatch(2);
        final long[] sizes = new long[2]; final boolean[] complete = new boolean[2];
        store.getTileRegion(id, result -> {
            if (result.isValue()) {
                complete[0] = result.getValue().getRequiredResourceCount() > 0
                    && result.getValue().getCompletedResourceCount() >= result.getValue().getRequiredResourceCount();
                sizes[0] = result.getValue().getCompletedResourceSize();
            }
            finished.countDown();
        });
        manager.getStylePack(style, result -> {
            if (result.isValue()) {
                complete[1] = result.getValue().getRequiredResourceCount() > 0
                    && result.getValue().getCompletedResourceCount() >= result.getValue().getRequiredResourceCount();
                sizes[1] = result.getValue().getCompletedResourceSize();
            }
            finished.countDown();
        });
        if (!finished.await(10, TimeUnit.SECONDS)) return status("unverified", 0, 0, false);
        boolean all = complete[0] && complete[1];
        return status(all ? "complete" : "missing", sizes[0] + sizes[1], all ? 1 : 0, all);
    }
    private static String status(String phase, long bytes, double fraction, boolean complete) {
        try { return new JSONObject().put("phase", phase).put("bytes", Math.max(0, bytes))
            .put("fraction", Math.max(0, Math.min(1, fraction))).put("complete", complete).toString(); }
        catch (org.json.JSONException error) { throw new IllegalStateException("Invalid map status"); }
    }
    private static final class Job {
        private String phase = "running";
        private long styleCount, styleRequired, styleBytes, tileCount, tileRequired, tileBytes;
        private boolean styleDone, tilesDone;
        private Cancelable styleTask, tileTask;
        synchronized boolean running() { return phase.equals("running"); }
        void setStyleTask(Cancelable task) { boolean stop; synchronized(this) { styleTask = task; stop = !running(); } if (stop) task.cancel(); }
        void setTileTask(Cancelable task) { boolean stop; synchronized(this) { tileTask = task; stop = !running(); } if (stop) task.cancel(); }
        synchronized void style(long count, long required, long bytes, boolean done) {
            if (!running()) return;
            styleCount = count; styleRequired = required; styleBytes = bytes; styleDone = done; finish();
        }
        synchronized void tiles(long count, long required, long bytes, boolean done) {
            if (!running()) return;
            tileCount = count; tileRequired = required; tileBytes = bytes; tilesDone = done; finish();
        }
        private void finish() { if (styleDone && tilesDone) phase = "complete"; }
        void fail() { stop("failed"); }
        void cancel() { stop("cancelled"); }
        private void stop(String reason) {
            Cancelable style, tiles;
            synchronized(this) { if (!running()) return; phase = reason; style = styleTask; tiles = tileTask; }
            if (style != null) style.cancel(); if (tiles != null) tiles.cancel();
        }
        synchronized String json() {
            double fraction = ((styleRequired > 0 ? (double) styleCount / styleRequired : 0)
                + (tileRequired > 0 ? (double) tileCount / tileRequired : 0)) / 2;
            return status(phase, styleBytes + tileBytes, phase.equals("complete") ? 1 : fraction, phase.equals("complete"));
        }
    }
}
