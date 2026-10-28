using NexoRuta.Mobile.Core.Rutas;
namespace NexoRuta.Mobile.Captura;
public sealed class RendicionPage : ContentPage
{
    private readonly TaskCompletionSource<Guid[]?> resultado = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<Guid[]?> Resultado => resultado.Task;
    public RendicionPage(IReadOnlyList<BultoRuta> bultos)
    {
        Title = "Bultos que devolvés";
        var seleccionados = new HashSet<Guid>();
        var lista = new VerticalStackLayout { Spacing = 12 };
        var estado = new Label { Text = "Seleccioná únicamente los bultos que entregás físicamente al depósito. El depósito confirmará su recepción." };
        foreach (var bulto in bultos)
        {
            var marcar = new CheckBox();
            SemanticProperties.SetDescription(marcar, $"Bulto {bulto.Codigo}");
            SemanticProperties.SetHint(marcar, "Marcá para declarar que lo entregás físicamente al depósito.");
            marcar.CheckedChanged += (_, e) => { if (e.Value) seleccionados.Add(bulto.Id); else seleccionados.Remove(bulto.Id); };
            lista.Add(new HorizontalStackLayout { Spacing = 10, Children = { marcar, new Label { Text = bulto.Codigo, VerticalOptions = LayoutOptions.Center, FontSize = 18 } } });
        }
        var confirmar = new Button { Text = "Confirmar bultos seleccionados" };
        confirmar.Clicked += async (_, _) =>
        {
            if (seleccionados.Count == 0) { estado.Text = "Seleccioná al menos un bulto para declarar la rendición."; return; }
            resultado.TrySetResult(seleccionados.ToArray()); await Navigation.PopAsync();
        };
        var grid = new Grid { Padding = 20, RowSpacing = 14, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        grid.Add(estado, 0, 0); grid.Add(new ScrollView { Content = lista }, 0, 1); grid.Add(confirmar, 0, 2); Content = grid;
    }
    protected override void OnDisappearing() { base.OnDisappearing(); resultado.TrySetResult(null); }
}
