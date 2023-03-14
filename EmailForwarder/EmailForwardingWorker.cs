namespace EmailForwarder
{
    public class EmailForwardingWorker : BackgroundService
    {
        private readonly List<EmailForwarder> _forwarders;
        private readonly AppSettings _settings;
        private readonly ILogger<EmailForwardingWorker> _logger;

        public EmailForwardingWorker(IEnumerable<EmailForwarder> forwarders,AppSettings settings, ILogger<EmailForwardingWorker> logger)
        {
            _forwarders = forwarders.ToList();
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await InitializeForwarders(stoppingToken);

            if (!_forwarders.Any())
            {
                _logger.LogInformation("All forwarders have failed to initialize. Exiting");
                await base.StopAsync(stoppingToken);
                Environment.Exit(1);
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                await ExecuteForwarders(stoppingToken);

                await Task.Delay(_settings.DelayMilliseconds, stoppingToken);
            }
        }

        private async Task InitializeForwarders(CancellationToken stoppingToken)
        {
            var failedForwarders = new List<EmailForwarder>();
            foreach (var forwarder in _forwarders)
            {
                try
                {
                    await forwarder.Init(stoppingToken);
                }
                catch(Exception ex)
                {
                    _logger.LogError(ex, $"Failed to initialize forwarder for email '{forwarder.ForwardingRule.SourceEmailAddress}'");
                    failedForwarders.Add(forwarder);
                }
            }

            foreach (var failedForwarder in failedForwarders)
            {
                _forwarders.Remove(failedForwarder);
            }
        }

        private async Task ExecuteForwarders(CancellationToken stoppingToken)
        {
            Task CreateForwardingTask(EmailForwarder x)
            {
                return Task.Factory.StartNew(async () => await x.ForwardMessages(stoppingToken), stoppingToken,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            }

            
            _logger.LogInformation($"Running {_forwarders.Count} EmailForwarders");

            List<(Task Task, EmailForwarder Forwarder)> tasks = new();
            try
            {
                tasks = _forwarders.Select(x => (Task: CreateForwardingTask(x), Forwarder: x)).ToList();

                await Task.WhenAll(tasks.Select(x => x.Task));

                _logger.LogInformation(
                    $"{tasks.Count(x => x.Task.Status == TaskStatus.RanToCompletion)} EmailForwarders have finished successfully");
            }
            catch (Exception ex)
            {
                var failedTasks = tasks.Where(x => x.Task.Status != TaskStatus.RanToCompletion).ToList();
                _logger.LogError(ex, $"{failedTasks.Count} EmailForwarders have finished with an error");

                if (failedTasks.Any())
                {
                    foreach (var failedTask in failedTasks)
                    {
                        _logger.LogError(failedTask.Task.Exception,
                            $"EmailForwarder {failedTask.Forwarder.ForwardingRule.SourceEmailAddress} has failed");
                    }
                }
            }
        }
    }
}