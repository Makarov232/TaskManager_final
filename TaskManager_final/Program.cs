using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace TaskManagerApp;

public sealed record TaskItem(
    int Id,
    string Title,
    string Description,
    int Priority);

public static class TraceManager
{
    public static readonly TraceSource Source =
        new("TaskManager.Trace");

    public static void Configure()
    {
        Directory.CreateDirectory("logs");

        Source.Switch = new SourceSwitch("TaskManagerSwitch")
        {
            Level = SourceLevels.All
        };

        Source.Listeners.Clear();

        Source.Listeners.Add(
            new ConsoleTraceListener());

        Source.Listeners.Add(
            new TextWriterTraceListener(
                Path.Combine("logs", "trace.log")));
    }

    public static void Write(
        TraceEventType eventType,
        string message)
    {
        Source.TraceEvent(
            eventType,
            0,
            message);

        foreach (TraceListener listener in Source.Listeners)
        {
            listener.Flush();
        }
    }

    public static void Close()
    {
        foreach (TraceListener listener in Source.Listeners)
        {
            listener.Flush();
            listener.Close();
        }

        Source.Close();
    }
}

public sealed class OperationTrace : IDisposable
{
    private readonly string _operation;
    private readonly Guid _operationId;
    private readonly Stopwatch _stopwatch;
    private bool _completed;

    public string Result { get; set; } = "Успех";

    private OperationTrace(
        string operation,
        Guid operationId)
    {
        _operation = operation;
        _operationId = operationId;
        _stopwatch = Stopwatch.StartNew();

        TraceManager.Write(
            TraceEventType.Start,
            $"Start: операция {_operation}, " +
            $"OperationId={_operationId}");
    }

    public static OperationTrace Start(
        string operation,
        Guid operationId)
    {
        return new OperationTrace(
            operation,
            operationId);
    }

    public void Dispose()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _stopwatch.Stop();

        TraceManager.Write(
            TraceEventType.Stop,
            $"Stop: операция {_operation}, " +
            $"OperationId={_operationId}, " +
            $"Результат={Result}, " +
            $"Время={_stopwatch.Elapsed.TotalMilliseconds:F2} мс");
    }
}

internal static class Program
{
    private static readonly List<TaskItem> Tasks = new();

    private static int _nextId = 1;

    private static void Main()
    {
        int exitCode = 0;
        OperationTrace? sessionTrace = null;

        try
        {
            ConfigureLogging();
            TraceManager.Configure();

            Log.Information(
                "Программа TaskManager запущена");

            sessionTrace = OperationTrace.Start(
                "Session",
                Guid.NewGuid());

            bool isRunning = true;

            while (isRunning)
            {
                Console.WriteLine();
                Console.Write(
                    "Команда (add, remove, list, help, exit): ");

                string? input = Console.ReadLine();

                if (input is null)
                {
                    Log.Information(
                        "Поток ввода закрыт");

                    break;
                }

                string command =
                    input.Trim().ToLowerInvariant();

                Log.Debug(
                    "Получена команда {Command}",
                    command);

                switch (command)
                {
                    case "add":
                        AddTask();
                        break;

                    case "remove":
                        RemoveTask();
                        break;

                    case "list":
                        ListTasks();
                        break;

                    case "help":
                        ShowHelp();
                        break;

                    case "exit":
                        Log.Information(
                            "Пользователь завершает работу");

                        isRunning = false;
                        break;

                    default:
                        Log.Warning(
                            "Неизвестная команда {Command}",
                            command);

                        Console.WriteLine(
                            "Неизвестная команда.");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            exitCode = 1;

            if (sessionTrace is not null)
            {
                sessionTrace.Result = "Ошибка";
            }

            Log.Fatal(
                ex,
                "Необработанная ошибка в основном цикле");

            Console.WriteLine(
                "Произошла критическая ошибка. " +
                "Подробности записаны в журнал.");
        }
        finally
        {
            Log.Information(
                "Программа завершается");

            sessionTrace?.Dispose();

            TraceManager.Write(
                TraceEventType.Stop,
                "Конец сессии TaskManager");

            TraceManager.Close();

            Log.CloseAndFlush();

            Environment.ExitCode = exitCode;
        }
    }

    private static void ConfigureLogging()
    {
        Directory.CreateDirectory("logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.WithProperty(
                "Application",
                "TaskManager")
            .Enrich.WithProperty(
                "Environment",
                "Development")
            .WriteTo.Console(
                outputTemplate:
                "[{Timestamp:HH:mm:ss} " +
                "{Level:u3}] {Message:lj}" +
                "{NewLine}{Exception}")
            .WriteTo.File(
                new RenderedCompactJsonFormatter(),
                "logs/taskmanager-.json",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();
    }

    private static void AddTask()
    {
        Guid operationId = Guid.NewGuid();

        using OperationTrace trace =
            OperationTrace.Start(
                "Add",
                operationId);

        ILogger logger = Log
            .ForContext("Operation", "Add")
            .ForContext("OperationId", operationId);

        try
        {
            logger.Information(
                "Начало добавления задачи");

            Console.Write(
                "Введите название задачи: ");

            string title =
                Console.ReadLine()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentException(
                    "Название задачи не может быть пустым.");
            }

            Console.Write(
                "Введите описание задачи " +
                "(необязательно): ");

            string description =
                Console.ReadLine()?.Trim() ?? string.Empty;

            Console.Write(
                "Введите приоритет от 1 до 5 " +
                "(необязательно): ");

            string priorityInput =
                Console.ReadLine()?.Trim() ?? string.Empty;

            int priority = 0;

            if (!string.IsNullOrWhiteSpace(priorityInput))
            {
                if (!int.TryParse(
                        priorityInput,
                        out priority))
                {
                    logger.Warning(
                        "Введён некорректный приоритет " +
                        "{PriorityInput}. Установлено значение 0",
                        priorityInput);

                    priority = 0;
                }
                else if (priority < 1 || priority > 5)
                {
                    logger.Warning(
                        "Приоритет {Priority} находится " +
                        "вне диапазона от 1 до 5",
                        priority);

                    priority = Math.Clamp(priority, 1, 5);
                }
            }

            TaskItem task = new(
                _nextId++,
                title,
                description,
                priority);

            Tasks.Add(task);

            logger.Information(
                "Задача добавлена {@Task}. " +
                "Всего задач: {TaskCount}",
                task,
                Tasks.Count);

            Console.WriteLine(
                "Задача успешно добавлена.");

            trace.Result = "Успех";
        }
        catch (Exception ex)
        {
            trace.Result = "Ошибка";

            TraceManager.Write(
                TraceEventType.Error,
                $"Ошибка операции Add: {ex.Message}");

            logger.Error(
                ex,
                "Ошибка при добавлении задачи. " +
                "Сообщение: {ErrorMessage}",
                ex.Message);

            Console.WriteLine(
                $"Ошибка: {ex.Message}");

            Console.WriteLine(
                "Подробности записаны в logs.");
        }
    }

    private static void RemoveTask()
    {
        Guid operationId = Guid.NewGuid();

        using OperationTrace trace =
            OperationTrace.Start(
                "Remove",
                operationId);

        ILogger logger = Log
            .ForContext("Operation", "Remove")
            .ForContext("OperationId", operationId);

        try
        {
            logger.Information(
                "Начало удаления задачи");

            Console.Write(
                "Введите ID или название задачи: ");

            string searchValue =
                Console.ReadLine()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(searchValue))
            {
                throw new ArgumentException(
                    "ID или название задачи не указаны.");
            }

            TaskItem? task = null;

            if (int.TryParse(
                    searchValue,
                    out int taskId))
            {
                task = Tasks.FirstOrDefault(
                    item => item.Id == taskId);
            }
            else
            {
                task = Tasks.FirstOrDefault(
                    item => string.Equals(
                        item.Title,
                        searchValue,
                        StringComparison.OrdinalIgnoreCase));
            }

            if (task is null)
            {
                trace.Result = "Ошибка";

                logger.Error(
                    "Ошибка удаления: задача " +
                    "{SearchValue} не найдена",
                    searchValue);

                Console.WriteLine(
                    "Ошибка: задача не найдена.");

                return;
            }

            Tasks.Remove(task);

            logger.Information(
                "Задача удалена {@Task}. " +
                "Осталось задач: {TaskCount}",
                task,
                Tasks.Count);

            Console.WriteLine(
                "Задача успешно удалена.");

            trace.Result = "Успех";
        }
        catch (Exception ex)
        {
            trace.Result = "Ошибка";

            TraceManager.Write(
                TraceEventType.Error,
                $"Ошибка операции Remove: {ex.Message}");

            logger.Error(
                ex,
                "Ошибка при удалении задачи. " +
                "Сообщение: {ErrorMessage}",
                ex.Message);

            Console.WriteLine(
                $"Ошибка: {ex.Message}");

            Console.WriteLine(
                "Подробности записаны в logs.");
        }
    }

    private static void ListTasks()
    {
        Guid operationId = Guid.NewGuid();

        using OperationTrace trace =
            OperationTrace.Start(
                "List",
                operationId);

        ILogger logger = Log
            .ForContext("Operation", "List")
            .ForContext("OperationId", operationId);

        try
        {
            logger.Information(
                "Начало вывода списка задач");

            if (Tasks.Count == 0)
            {
                logger.Warning(
                    "Список задач пуст");

                Console.WriteLine(
                    "Список задач пуст.");

                trace.Result = "Предупреждение";
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Список задач:");

            foreach (TaskItem task in Tasks)
            {
                Console.WriteLine(
                    $"ID: {task.Id} | " +
                    $"Название: {task.Title} | " +
                    $"Описание: {task.Description} | " +
                    $"Приоритет: {task.Priority}");
            }

            logger.Information(
                "Показан список задач. " +
                "Количество: {TaskCount}",
                Tasks.Count);

            trace.Result = "Успех";
        }
        catch (Exception ex)
        {
            trace.Result = "Ошибка";

            TraceManager.Write(
                TraceEventType.Error,
                $"Ошибка операции List: {ex.Message}");

            logger.Error(
                ex,
                "Ошибка при выводе списка задач. " +
                "Сообщение: {ErrorMessage}",
                ex.Message);

            Console.WriteLine(
                $"Ошибка: {ex.Message}");

            Console.WriteLine(
                "Подробности записаны в logs.");
        }
    }

    private static void ShowHelp()
    {
        Log.Debug(
            "Показана справка пользователю");

        Console.WriteLine();
        Console.WriteLine("Доступные команды:");
        Console.WriteLine(
            "add    - добавить задачу");
        Console.WriteLine(
            "remove - удалить задачу по ID или названию");
        Console.WriteLine(
            "list   - показать список задач");
        Console.WriteLine(
            "help   - показать справку");
        Console.WriteLine(
            "exit   - завершить программу");
    }
}