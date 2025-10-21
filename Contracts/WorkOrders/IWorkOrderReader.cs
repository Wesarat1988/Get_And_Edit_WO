using System.Threading;
using System.Threading.Tasks;
using Contracts.Common;

namespace Contracts.WorkOrders;

/// <summary>
/// Provides read-only access to work order information.
/// </summary>
public interface IWorkOrderReader
{
    Task<PageResult<WorkOrderDto>> SearchAsync(PageRequest request, CancellationToken cancellationToken = default);

    Task<WorkOrderDto?> GetAsync(string id, CancellationToken cancellationToken = default);
}
