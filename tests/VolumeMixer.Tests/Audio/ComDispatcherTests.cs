using VolumeMixer.Audio;

namespace VolumeMixer.Tests.Audio;

public class ComDispatcherTests
{
    [Fact]
    public void Invoke_executa_na_thread_MTA_e_retorna_resultado()
    {
        using var dispatcher = new ComDispatcher();
        var result = dispatcher.Invoke(() =>
        {
            return Thread.CurrentThread.GetApartmentState();
        });
        Assert.Equal(ApartmentState.MTA, result);
    }

    [Fact]
    public void Invoke_funciona_com_valores_genericos()
    {
        using var dispatcher = new ComDispatcher();
        var result = dispatcher.Invoke(() => 42);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Invoke_string_retorna_corretamente()
    {
        using var dispatcher = new ComDispatcher();
        var result = dispatcher.Invoke(() => "hello");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Invoke_relanca_excecao_do_lambda()
    {
        using var dispatcher = new ComDispatcher();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            dispatcher.Invoke(() => throw new InvalidOperationException("erro teste")));
        Assert.Equal("erro teste", ex.Message);
    }

    [Fact]
    public void Invoke_action_executa_sem_erro()
    {
        using var dispatcher = new ComDispatcher();
        var executed = false;
        dispatcher.Invoke(() => executed = true);
        Assert.True(executed);
    }

    [Fact]
    public void Invoke_action_relanca_excecao()
    {
        using var dispatcher = new ComDispatcher();
        Assert.Throws<NullReferenceException>(() =>
            dispatcher.Invoke(() => { throw new NullReferenceException(); }));
    }

    [Fact]
    public async Task Post_executa_fire_and_forget()
    {
        using var dispatcher = new ComDispatcher();
        var tcs = new TaskCompletionSource<bool>();
        dispatcher.Post(() => tcs.SetResult(true));
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(tcs.Task, completed);
        var result = await tcs.Task;
        Assert.True(result);
    }

    [Fact]
    public void Multiplas_invocacoes_sequenciais_funcionam()
    {
        using var dispatcher = new ComDispatcher();
        for (var i = 0; i < 50; i++)
        {
            var capture = i;
            var result = dispatcher.Invoke(() => capture * 2);
            Assert.Equal(capture * 2, result);
        }
    }

    [Fact]
    public void Dispose_encerra_a_thread()
    {
        var dispatcher = new ComDispatcher();
        var thread = dispatcher.MtaThread;
        Assert.True(thread.IsAlive);
        dispatcher.Dispose();
        // After Dispose, the thread should already be joined (dead).
        Assert.False(thread.IsAlive);
    }

    [Fact]
    public void Invoke_apos_Dispose_lanca_ObjectDisposedException()
    {
        var dispatcher = new ComDispatcher();
        dispatcher.Dispose();
        Assert.Throws<ObjectDisposedException>(() => dispatcher.Invoke(() => 1));
    }

    [Fact]
    public void Invoke_apos_Dispose_action_lanca_ObjectDisposedException()
    {
        var dispatcher = new ComDispatcher();
        dispatcher.Dispose();
        Assert.Throws<ObjectDisposedException>(() => dispatcher.Invoke(() => { }));
    }

    [Fact]
    public void Dispose_idempotente()
    {
        var dispatcher = new ComDispatcher();
        dispatcher.Dispose();
        dispatcher.Dispose(); // should not throw
    }

    [Fact]
    public void Post_apos_Dispose_nao_lanca()
    {
        var dispatcher = new ComDispatcher();
        dispatcher.Dispose();
        var ex = Record.Exception(() => dispatcher.Post(() => { }));
        Assert.Null(ex);
    }

    [Fact]
    public void MtaThread_property_retorna_a_thread_correta()
    {
        using var dispatcher = new ComDispatcher();
        var threadFromDispatcher = dispatcher.MtaThread;
        Assert.NotNull(threadFromDispatcher);
        Assert.Equal(ApartmentState.MTA, threadFromDispatcher.GetApartmentState());
        Assert.True(threadFromDispatcher.IsBackground);
        Assert.Equal("CoreAudio-MTA", threadFromDispatcher.Name);
    }
}
