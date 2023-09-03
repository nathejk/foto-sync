using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace FotoSync;

[DisallowConcurrentExecution]
public sealed class Synchronize : IJob
{
    private readonly HttpClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<Synchronize> _logger;

    public Synchronize(
        IHttpClientFactory factory,
        IConfiguration configuration,
        ILogger<Synchronize> logger
    )
    {
        _configuration = configuration;
        _logger = logger;
        _client = factory.CreateClient(nameof(Synchronize));
        _client.BaseAddress = _configuration.GetValue<Uri>("PhotoHost");
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Starter download");

        var type = _configuration.GetValue<string>("PhotoType");
        var directory =
            $"{_configuration.GetValue<string>("DestinationFolder")!.TrimEnd(Path.DirectorySeparatorChar)}{Path.DirectorySeparatorChar}{type}";
        var logfile =
            $"{directory.TrimEnd(Path.DirectorySeparatorChar)}{Path.DirectorySeparatorChar}log.json";

        _logger.LogInformation("Skriver download-log til {LogFile}", logfile);

        Directory.CreateDirectory(directory);

        var log = new LogFile(new List<LogEntry>());

        try
        {
            log =
                JsonConvert.DeserializeObject<LogFile>(await File.ReadAllTextAsync(logfile))
                ?? new LogFile(new List<LogEntry>());
        }
        catch
        {
            // ignored
        }

        var req = await _client.GetAsync($"/api/photo/list/{type}");
        if (!req.IsSuccessStatusCode)
        {
            _logger.LogWarning("Kunne ikke hente billeder, fik statuskode: {StatusCode}", req.StatusCode);
            return;
        }

        var photos = JsonConvert.DeserializeObject<IReadOnlyList<Uri>>(
            await req.Content.ReadAsStringAsync()
        );

        if (photos is null)
        {
            _logger.LogWarning("Fandt ingen filer!");
            return;
        }

        foreach (var photo in photos)
        {
            var fileName = Path.GetFileName(photo.LocalPath);

            _logger.LogInformation("Tjekker billede: {Billede}", fileName);

            if (log.LogEntries.Any(x => x.Filename == fileName))
            {
                _logger.LogDebug("Fil allerede hentet");
                continue;
            }

            var photoRequest = await _client.GetAsync(photo);
            if (!photoRequest.IsSuccessStatusCode)
            {
                _logger.LogError("Fejl ved hentning af billede");
                continue;
            }

            if (
                File.Exists(
                    $"{directory.TrimEnd(Path.DirectorySeparatorChar)}{Path.DirectorySeparatorChar}{fileName}"
                )
            )
            {
                _logger.LogWarning(
                    "Fil eksisterer i mappe men ikke i log. Tilføjer til log og fortsætter"
                );
                log.LogEntries.Add(new LogEntry(fileName));
                continue;
            }

            await using (
                var fs = new FileStream(
                    $"{directory.TrimEnd(Path.DirectorySeparatorChar)}{Path.DirectorySeparatorChar}{fileName}",
                    FileMode.CreateNew
                )
            )
            {
                await photoRequest.Content.CopyToAsync(fs);
                log.LogEntries.Add(new LogEntry(fileName));
            }

            _logger.LogInformation("Færdig med at hente billede");
        }

        await File.WriteAllTextAsync(
            logfile,
            JsonConvert.SerializeObject(log, Formatting.Indented)
        );
        
        _logger.LogInformation("Færdig for nu - starter igen om 30 sekunder");
    }

    private record LogFile(List<LogEntry> LogEntries);

    private record LogEntry
    {
        public LogEntry(string fileName)
        {
            Filename = fileName;
            DownloadedAt = DateTime.UtcNow;
        }

        public string Filename { get; private set; }

        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        private DateTime DownloadedAt { get; set; }
    }
}
