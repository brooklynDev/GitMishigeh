using System.Threading;
using System.Threading.Tasks;
using GitMishigeh.Models;

namespace GitMishigeh.Services;

public interface IGitService
{
    Task<GitRepositoryState> GetRepositoryStateAsync(string repositoryPath, CancellationToken cancellationToken = default);

    Task<string> StageAllAsync(string repositoryPath, CancellationToken cancellationToken = default);

    Task<string> UnstageAllAsync(string repositoryPath, CancellationToken cancellationToken = default);

    Task<string> CommitAsync(string repositoryPath, string commitMessage, CancellationToken cancellationToken = default);
}
