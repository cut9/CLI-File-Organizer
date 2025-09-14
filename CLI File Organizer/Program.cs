using Mono.Options;
using System.Globalization;
using System.Text.Json;

bool showHelp = false;
bool dryRunMode = false;
bool successCopy = false;
string? path = null;
string? sortMode = null;
string otherExtension = "Other";
int filesSorted = 0;
var cfg = LoadConfig();

Dictionary<string, List<string>>? extensionDictionary = new Dictionary<string, List<string>>();
var sizeDictionary = new Dictionary<int, string>();

if (cfg.Extensions != null && cfg.Extensions.Count > 0)
    extensionDictionary = cfg.Extensions;
if (!string.IsNullOrEmpty(cfg.OtherExtension))
    otherExtension = cfg.OtherExtension;
if (cfg.Sizes != null && cfg.Sizes.Count > 0)
    sizeDictionary = cfg.Sizes.ToDictionary(kvp => int.Parse(kvp.Key), kvp => kvp.Value);

if (extensionDictionary.Count == 0)
{
    extensionDictionary["Images"] = new List<string> { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };
    extensionDictionary["Videos"] = new List<string> { ".mp4", ".avi", ".mov" };
    extensionDictionary["Audio"] = new List<string> { ".mp3", ".wav", ".flac" };
    extensionDictionary["Documents"] = new List<string> { ".pdf", ".docx", ".txt" };
    extensionDictionary["Archives"] = new List<string> { ".zip", ".rar", ".7z" };
    extensionDictionary["Programs"] = new List<string> { ".exe", ".msi" };
}

if (sizeDictionary.Count == 0)
{
    sizeDictionary[2] = "Tiny";
    sizeDictionary[32] = "Small";
    sizeDictionary[256] = "Medium";
    sizeDictionary[1024] = "Big";
    sizeDictionary[8192] = "Large";
    sizeDictionary[32768] = "Huge";
    sizeDictionary[int.MaxValue] = "Massive";
}

SaveConfig();

if (!string.IsNullOrEmpty(otherExtension))
{
    extensionDictionary.Add(otherExtension, new List<string> { "" });
}

Dictionary<string, int> sortStatisticDictionary = new Dictionary<string, int>();

var options = new OptionSet {
    { "by=", "Сортировка по категориям: 'extension' - сортировать по расширениям, 'date' - сортировать по дате последнего изменения, 'size' - сортировать по размеру.", v => sortMode = v },
    { "dry-run", "Только показать, куда переместятся файлы, без реального перемещения.", v => dryRunMode = true },
    { "help", "Показать справку.", v => showHelp = v != null },
};

List<string> extra;
try
{
    extra = options.Parse(args);
}
catch (OptionException e)
{
    Console.WriteLine("Ошибка: " + e.Message);
    Console.WriteLine("Попробуйте `--help` чтобы получить больше информации.");
    return;
}

if (showHelp)
{
    ShowHelp(options);
    return;
}

if (extra.Count > 0 && string.IsNullOrEmpty(path))
    path = extra[0];

if (string.IsNullOrEmpty(path))
{
    Console.WriteLine("Не указан путь. Пример:");
    Console.WriteLine(@"""C:\Users\Admin\Downloads"" --by size");
    ShowHelp(options);
    return;
}

if (!Directory.Exists(path))
{
    Console.WriteLine($"Указанный путь не найден: {path}");
    return;
}

if (!string.IsNullOrEmpty(sortMode))
{
    SortFiles();
}

void SortFiles()
{
    IEnumerable<string>? dir = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly);
    switch (sortMode)
    {
        case "extension":
            sortStatisticDictionary = extensionDictionary.ToDictionary(kvp => kvp.Key, kvp => 0);
            foreach (var file in dir)
            {
                OrganizeByExtension(file);
            }
            break;
        case "date":
            foreach (var file in dir)
            {
                OrganizeByDate(file);
            }
            break;
        case "size":
            sortStatisticDictionary = sizeDictionary.ToDictionary(kvp => kvp.Value, kvp => 0);
            foreach (var file in dir)
            {
                OrganizeBySize(file);
            }
            break;
        default:
            Console.WriteLine($"Неизвестный режим сортировки: {sortMode}");
            return;
    }
    ShowSortStatistic();
}

void OrganizeByExtension(string file)
{
    foreach (var item in extensionDictionary)
    {
        for (int i = 0; i < item.Value.Count; i++)
        {
            if (Path.GetExtension(file).Equals(item.Value[i], StringComparison.OrdinalIgnoreCase))
            {
                MoveFile(file, Path.Combine(path, item.Key));
                if (successCopy)
                    sortStatisticDictionary[item.Key]++;
                return;
            }
            else if (item.Key == otherExtension)
            {
                MoveFile(file, directoryPath: Path.Combine(path, item.Key));
                if (successCopy)
                    sortStatisticDictionary[item.Key]++;
            }
        }
    }
}

void OrganizeByDate(string file)
{
    var monthName = new CultureInfo("en-US").DateTimeFormat.MonthGenitiveNames;
    DateTime date = new FileInfo(file).LastWriteTime;
    string yearPath = Path.Combine(path, date.Year.ToString());
    string monthPath = Path.Combine(yearPath, monthName[date.Month - 1]);
    string yearMonthKey = $"{date.Year}\\{monthName[date.Month - 1]}";
    MoveFile(file, monthPath);
    if (!successCopy)
        return;
    if (sortStatisticDictionary.ContainsKey(yearMonthKey))
        sortStatisticDictionary[yearMonthKey]++;
    else
        sortStatisticDictionary.Add(yearMonthKey, 1);
}

void OrganizeBySize(string file)
{
    long sizeBytes = new FileInfo(file).Length;
    int sizeMB = (int)(sizeBytes / (1024 * 1024));
    foreach (var item in sizeDictionary)
    {
        if (sizeMB < item.Key)
        {
            MoveFile(file, directoryPath: Path.Combine(path, item.Value));
            if (successCopy)
                sortStatisticDictionary[item.Value]++;
            return;
        }
    }
}

void MoveFile(string file, string directoryPath)
{
    try
    {
        if (dryRunMode)
        {
            Console.WriteLine($"[Dry-Run] Файл {Path.GetFileName(file)} будет перемещён в {directoryPath}");
            successCopy = true;
            filesSorted++;
            return;
        }
        Directory.CreateDirectory(directoryPath);
        string fileAfterMove = Path.Combine(directoryPath, Path.GetFileName(file));
        if (File.Exists(fileAfterMove))
        {
            var name = Path.GetFileNameWithoutExtension(fileAfterMove);
            var ext = Path.GetExtension(fileAfterMove);
            int i = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(directoryPath, $"{name} ({i}){ext}");
                i++;
            } while (File.Exists(candidate));
            fileAfterMove = candidate;
        }
        File.Move(file, fileAfterMove);
        filesSorted++;
        successCopy = true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка при перемещении {Path.GetFileName(file)}: \n{ex.Message}\n");
        successCopy = false;
    }
}

void ShowSortStatistic()
{
    Console.WriteLine($"Всего отсортировано файлов: {filesSorted}");
    if (sortStatisticDictionary.Count > 0)
    {
        Console.WriteLine("Статистика по категориям:");
        foreach (var item in sortStatisticDictionary)
        {
            if (item.Value > 0)
                Console.WriteLine($"{item.Value} -> [{item.Key}]");
        }
    }
    filesSorted = 0;
    sortStatisticDictionary.Clear();
}

static void ShowHelp(OptionSet opts)
{
    Console.WriteLine("Использование: .\\\"CLI File Organizer.exe\" [path] [опции]");
    Console.WriteLine("Аргументы и опции:");
    opts.WriteOptionDescriptions(Console.Out);
}

void SaveConfig()
{
    JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

    var cfg = new Config
    {
        Extensions = extensionDictionary,
        OtherExtension = otherExtension,
        Sizes = sizeDictionary.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value)
    };

    var json = JsonSerializer.Serialize(cfg, JsonOptions);
    File.WriteAllText(configPath, json);
}

Config LoadConfig()
{
    string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

    if (!File.Exists(configPath))
        return new Config();

    var json = File.ReadAllText(configPath);

    try
    {
        return JsonSerializer.Deserialize<Config>(json) ?? new Config();
    }
    catch (JsonException ex)
    {
        Console.WriteLine($"Ошибка парсинга JSON: {ex.Message}");
        return new Config();
    }
}

public class Config
{
    public Dictionary<string, List<string>> Extensions { get; set; } = new();
    public string OtherExtension { get; set; } = string.Empty;
    public Dictionary<string, string> Sizes { get; set; } = new();
}