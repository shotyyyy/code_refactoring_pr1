using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RefactoringExample
{
    // Доменные модели
    public sealed record Request(string Path, string Method);
    public sealed record Response(int Status, string Content = "");

    // Логирование
    public enum LogLevel { Debug = 0, Info = 1, Warn = 2 }

    
    /// Простейший логгер с уровнями. По умолчанию - максимально подробный (не меняем текущее поведение).
    public static class Logger
    {
        public static LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

        public static void Debug(string message)
        {
            if (MinimumLevel <= LogLevel.Debug) Console.WriteLine(message);
        }

        public static void Info(string message)
        {
            if (MinimumLevel <= LogLevel.Info) Console.WriteLine(message);
        }

        public static void Warn(string message)
        {
            if (MinimumLevel <= LogLevel.Warn) Console.WriteLine(message);
        }
    }

    // Хранилище запросов — инкапсулируем коллекцию
    public sealed class RequestStore
    {
        private readonly List<object> _items = new();

        /// Количество элементов в хранилище.
        public int Count => _items.Count;

        /// Добавить элемент (поддерживаем прежнюю семантику object).
        public void Add(object item)
        {
            if (item is null) return;
            _items.Add(item);
        }
    }

    // Конфигурация сервисов
    public sealed class ServiceConfig
    {
        public int TimeoutSeconds { get; init; } = 30;
        public int RetryCount { get; init; } = 3;
        public string LogLevel { get; init; } = "info";
    }

    public static class ServiceConfigurator
    {
        public static void ConfigureService(string name, ServiceConfig config)
        {
            // Имитация конфигурации сервиса
            Logger.Info($"configuring service {name}");
            Logger.Debug(JsonSerializer.Serialize(config));
            // потенциально могли бы настраивать Logger.MinimumLevel на основе config.LogLevel
            // но оставляем текущее поведение неизменным (всё логируем).
        }
    }

    public enum DatabaseType { MySql, MongoDb }

    public static class DatabaseConnector
    {
        private static readonly Regex PasswordPairRegex =
            new(@"(?i)(Password|Pwd)=([^;]+)", RegexOptions.Compiled);

        private static readonly Regex UriCredentialRegex =
            // захватываем user:pass@ в любой схеме вида scheme://user:pass@host
            new(@"(?i)^(?<scheme>[a-z][a-z0-9+\-.]*://)(?<user>[^:/@]+):(?<pass>[^@]+)@(?<rest>.+)$",
                RegexOptions.Compiled);

        public static void Connect(DatabaseType type, string connectionString)
        {
            var masked = MaskSecrets(connectionString);
            var dbName = type switch
            {
                DatabaseType.MySql => "MySQL",
                DatabaseType.MongoDb => "MongoDB",
                _ => type.ToString()
            };

            Logger.Info($"connecting to {dbName} database...");
            Logger.Debug($"using connection string: {masked}");
            // тут могла бы быть реальная логика подключения
        }

        private static string MaskSecrets(string cs)
        {
            if (string.IsNullOrWhiteSpace(cs)) return cs;

            // 1) Маскируем user:pass@ в URI
            var uriMatch = UriCredentialRegex.Match(cs);
            if (uriMatch.Success)
            {
                var scheme = uriMatch.Groups["scheme"].Value;
                var user = uriMatch.Groups["user"].Value;
                var rest = uriMatch.Groups["rest"].Value;
                return $"{scheme}{user}:***@{rest}";
            }

            // 2) Маскируем Password/Pwd в паре ключ=значение
            cs = PasswordPairRegex.Replace(cs, m => $"{m.Groups[1].Value}=***");

            return cs;
        }
    }

    // Утилиты
    public static class Utils
    {
        
        /// Действительно глубокая копия для сериализуемых объектов.
        /// При неудаче — возвращает исходный объект, чтобы не менять поведение программы.
        public static T DeepCopy<T>(T obj)
        {
            if (obj is null) return obj!;
            try
            {
                var json = JsonSerializer.Serialize(obj);
                var clone = JsonSerializer.Deserialize<T>(json);
                return clone is null ? obj : clone;
            }
            catch
            {
                // сохраняем прошлое поведение программы: не падаем, возвращаем оригинал
                return obj;
            }
        }

        private static readonly Regex SafeExprRegex =
            new(@"^[0-9\s+\-*/().]+$", RegexOptions.Compiled);

        
        /// Безопасная оценка простых арифметических выражений (+-*/ и скобки).
        /// Возвращает object? для совместимости с имеющимся кодом.
        public static object? TryEvaluateArithmetic(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return null;

            // Разрешаем только цифры, пробелы, +-*/(). Иначе — отклоняем.
            if (!SafeExprRegex.IsMatch(expr)) return null;

            try
            {
                var table = new DataTable
                {
                    // чтобы числа с точкой не зависели от локали
                    Locale = CultureInfo.InvariantCulture
                };
                var result = table.Compute(expr, "");
                return result;
            }
            catch
            {
                return null;
            }
        }
    }

    public enum RequestKind { Api, Page }

    // Обработчик запросов — разделяем понятия и убираем дубли
    public sealed class RequestHandler
    {
        private readonly RequestStore _store;
        public RequestHandler(RequestStore store) => _store = store;

        public void Handle(Request req)
        {
            _store.Add(req);

            var payload = ExtractPayload(req);
            var processed = ProcessPayload(payload);

            var kind = DetermineKind(req);
            HandleByKind(kind, payload, processed);
        }

        private static object ExtractPayload(Request req)
        {
            // имитация получения значения из запроса
            return new { Value = "test" };
        }

        private static object ProcessPayload(object payload)
        {
            // имитация обработки значения
            return new { Result = "processed" };
        }

        private static RequestKind DetermineKind(Request req)
        {
            var isApi = req.Path?.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) == true;
            return isApi ? RequestKind.Api : RequestKind.Page;
        }

        private static void HandleByKind(RequestKind kind, object payload, object processed)
        {
            var context = kind == RequestKind.Api ? "handling API request" : "handling page request";
            LogValueAndResult(context, payload, processed);
        }

        private static void LogValueAndResult(string context, object payload, object processed)
        {
            Logger.Info(context);
            Logger.Debug($"value: {JsonSerializer.Serialize(payload)}");
            Logger.Debug($"result: {JsonSerializer.Serialize(processed)}");
        }
    }

    public class Program
    {
        private static readonly RequestStore requestStore = new();

        public static void Main(string[] args)
        {
            Logger.Info("starting application...");

            // Подключения к базам
            DatabaseConnector.Connect(DatabaseType.MySql, "Server=localhost;Database=test;User=root;Password=12345;");
            DatabaseConnector.Connect(DatabaseType.MongoDb, "mongodb://localhost:27017/test");

            // Обработка запроса
            var request = new Request("/api/data", "GET");
            var handler = new RequestHandler(requestStore);
            handler.Handle(request); // убрали неиспользуемый Response

            // Глубокая копия словаря
            var original = new Dictionary<string, object>
            {
                { "name", "test" },
                { "nested", new Dictionary<string, object> { { "value", 123 } } }
            };
            var copy = Utils.DeepCopy(original);
            Logger.Debug($"original: {JsonSerializer.Serialize(original)}");
            Logger.Debug($"copy: {JsonSerializer.Serialize(copy)}");

            // Оценка выражения
            var result = Utils.TryEvaluateArithmetic("2 + 3 * 4");
            Logger.Info($"eval result: {result}");

            // Конфигурация сервисов
            ServiceConfigurator.ConfigureService("A", new ServiceConfig { TimeoutSeconds = 30, RetryCount = 3, LogLevel = "debug" });
            ServiceConfigurator.ConfigureService("B", new ServiceConfig { TimeoutSeconds = 30, RetryCount = 3, LogLevel = "info" });

            // Демонстрационное накопление запросов — поведение сохранено
            for (int i = 0; i < 100; i++)
            {
                requestStore.Add(new { Id = i, Data = new string('x', 1000) });
            }
            Logger.Info($"requests count: {requestStore.Count}");

            Logger.Info("application completed.");
        }
    }
}