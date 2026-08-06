namespace Modulus.BackgroundJobs;

public interface IBackgroundJob<TArgs>
{
    Task ExecuteAsync(TArgs args, CancellationToken ct);
}
