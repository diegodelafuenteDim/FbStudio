using Avalonia.Headless;

namespace FbStudio.App.Tests;

internal static class HeadlessDispatch
{
    /// <summary>
    /// Corre un test async en el hilo de UI y lo espera hasta el final.
    /// <see cref="HeadlessUnitTestSession"/> no tiene una sobrecarga para <c>Func&lt;Task&gt;</c>: una lambda async sin valor
    /// cae en <c>Dispatch&lt;TResult&gt;(Func&lt;TResult&gt;)</c> con TResult = Task, y el test termina en el primer await sin
    /// ver los Assert que siguen. Devolviendo un valor se usa la sobrecarga de <c>Func&lt;Task&lt;TResult&gt;&gt;</c>, que sí espera.
    /// </summary>
    public static Task DispatchAsync(this HeadlessUnitTestSession session, Func<Task> test, CancellationToken ct) =>
        session.Dispatch(async () =>
        {
            await test();
            return true;
        }, ct);
}
