using System.Threading;
using System.Threading.Tasks;

namespace GitMishigeh.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}
