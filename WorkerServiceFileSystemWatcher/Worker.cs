using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;

namespace WorkerServiceFileSystemWatcher
{
    internal class CacheItemValue
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public int RetryCount { get; set; }
    }
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private FileSystemWatcher _fileSystemWatcher;
        private readonly MemoryCache _memCache;
        private readonly CacheItemPolicy _cacheItemPolicy;
        private const int CacheTimeSeconds = 10;
        private const int MaxRetries = 3;
        public Worker(ILogger<Worker> logger, IOptions<AppSettings> settings)
        {
            _logger = logger;
            _fileSystemWatcher = new FileSystemWatcher();
            _fileSystemWatcher.Path = settings.Value.Path;
            _fileSystemWatcher.EnableRaisingEvents = true;
            _memCache = MemoryCache.Default;
            _cacheItemPolicy = new CacheItemPolicy
            {
                RemovedCallback = OnRemovedFromCache
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Service Execute.");
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                await Task.Delay(5 * 60 * 1000, stoppingToken);
            }
            _logger.LogInformation("Service Execute stopped.");
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _fileSystemWatcher.Created += fileSystemWatcherOrigin_Created;
            return base.StartAsync(cancellationToken);
        }


        private void fileSystemWatcherOrigin_Created(object sender, System.IO.FileSystemEventArgs e)
        {
            _cacheItemPolicy.AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(CacheTimeSeconds);
            var fileData = new CacheItemValue()
            {
                FilePath = e.FullPath,
                RetryCount = 0,
                FileName = e.Name
            };

            _memCache.AddOrGetExisting(e.Name, fileData, _cacheItemPolicy);
        }


        protected static bool IsFileLocked(string filePath)
        {
            FileStream stream = null;
            var file = new FileInfo(filePath);

            try
            {
                stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return true;
            }
            finally
            {
                stream?.Close();
            }
            return false;
        }

        private void OnRemovedFromCache(CacheEntryRemovedArguments args)
        {
            // Checking if expired, for a bit of future-proofing
            if (args.RemovedReason != CacheEntryRemovedReason.Expired) return;

            var cacheItemValue = (CacheItemValue)args.CacheItem.Value;

            if (cacheItemValue.RetryCount > MaxRetries) return;

            // If file is locked send, it back into the cache again
            // Could make the expiration period scale exponentially on retries
            if (IsFileLocked(cacheItemValue.FilePath))
            {
                cacheItemValue.RetryCount++;
                _cacheItemPolicy.AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(CacheTimeSeconds);

                _memCache.Add(cacheItemValue.FileName, cacheItemValue, _cacheItemPolicy);
            }

            Console.WriteLine($"Now is a safe(ish) time to complete actions on file: {cacheItemValue.FileName}");
        }
    }
}