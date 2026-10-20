using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Messaging;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.Application.Timetable.GetService;

/// <summary>Backs <c>ITimetable.GetServiceAsync</c>: a service with its line, direction, stops and guard.</summary>
internal sealed class GetServiceQueryHandler(ITimetableRepository timetable)
    : IQueryHandler<GetServiceQuery, ServiceDto>
{
    private static readonly Error ServiceNotFound =
        Error.NotFound("service.not_found", "No service matches this identifier.");

    public async Task<Result<ServiceDto>> Handle(
        GetServiceQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var service = await timetable.GetServiceAsync(request.ServiceId, cancellationToken);

        return service is null
            ? Result.Failure<ServiceDto>(ServiceNotFound)
            : Result.Success(service);
    }
}
