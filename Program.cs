using ClipBoardX.Services;
using ClipBoardX.UI;

namespace ClipBoardX;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Global\ClipBoardX.SingleInstance";
    private const string ShowWindowEventName = @"Global\ClipBoardX.ShowWindow";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Мьютекс держится всё время работы процесса: запущен только один экземпляр.
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            // Просим уже работающий экземпляр показать окно истории.
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowWindowEventName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // Работающий экземпляр ещё не успел создать событие — просто выходим.
            }

            return;
        }

        var startMinimized = args.Any(a =>
            a.Equals("--autostart", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        try
        {
            using var host = new AppContextHost(startMinimized);
            using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);

            var signalThread = new Thread(() => WaitForShowRequest(host))
            {
                IsBackground = true,
                Name = "ClipBoardX.ShowRequest"
            };

            signalThread.Start();

            Application.Run(host);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"ClipBoardX не удалось запустить:\n\n{ex.Message}",
                "ClipBoardX",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void WaitForShowRequest(AppContextHost host)
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ShowWindowEventName);
            while (signal.WaitOne())
            {
                host.RequestShowWindow();
            }
        }
        catch (Exception)
        {
            // Событие недоступно — вторая копия просто ничего не сделает.
        }
    }
}