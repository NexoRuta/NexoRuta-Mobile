# Restore reproducible

`Directory.Build.props` selecciona el lock en cada proyecto del grafo:

- Sin RID: `packages.lock.json` (Core, Api, Storage, Application y pruebas net10.0).
- `android-arm64`: `packages.android.lock.json`.
- `android-x64`: `packages.android-x64.lock.json` (también el emulador local).

El lock x64 de la aplicación se renombró sin cambiar su contenido. Los proyectos
compartidos conservan net10.0 también al restaurar Android. No pasar
`TargetFramework=net10.0-android` globalmente al restore: modifica el framework de
los proyectos compartidos. El proyecto MAUI ya declara su framework Android.

```
dotnet restore tests/NexoRuta.Mobile.Application.Tests --locked-mode
dotnet restore src/NexoRuta.Mobile -p:RuntimeIdentifier=android-arm64 --locked-mode
dotnet build src/NexoRuta.Mobile -c Release -f net10.0-android -p:RuntimeIdentifier=android-arm64 --no-restore
```

Los builds con restore implícito también exigen locks. Los helpers del emulador
y de distribución fijan `RestoreLockedMode=true`. Cambiar de grafo requiere
restaurarlo antes de usar `--no-restore`: comparten obj pero nunca el lock.

Para actualizar dependencias deliberadamente, ejecutar los restores portables y
los dos restores MAUI (arm64 y x64) con `--force-evaluate
-p:RestoreLockedMode=false`, revisar todos los diffs y versionarlos juntos. Esa
excepción se usa exclusivamente para regenerar locks; CI y los builds normales
mantienen locked-mode. No borrar locks ni generar reemplazos temporales en obj.

El job Android restaura las cuatro suites portables antes y después de compilar,
y verifica que ningún lock cambió. Esto cubre la contaminación entre grafos.
