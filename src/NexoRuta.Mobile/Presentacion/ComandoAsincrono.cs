using System.Windows.Input;
namespace NexoRuta.Mobile.Presentacion;

public sealed class ComandoAsincrono(Func<Task> ejecutar, Func<bool>? permitido = null) : ICommand
{
    private bool ejecutando;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !ejecutando && (permitido?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        ejecutando = true; Actualizar();
        try { await ejecutar(); }
        finally { ejecutando = false; Actualizar(); }
    }
    public void Actualizar() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
