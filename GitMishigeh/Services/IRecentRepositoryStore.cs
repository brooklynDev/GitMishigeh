using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GitMishigeh.Models;

namespace GitMishigeh.Services;

public interface IRecentRepositoryStore
{
    Task<IReadOnlyList<RecentRepository>> LoadAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecentRepository>> AddOrUpdateAsync(string repositoryPath, CancellationToken cancellationToken = default);
}
