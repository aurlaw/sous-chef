using Microsoft.AspNetCore.Http;

namespace SousChef.Core.Common;

public static class ResultExtensions
{
    public static IResult ToApiResult<T>(this Result<T> result) =>
        result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error!.Code switch
            {
                "NOT_FOUND"  => Results.NotFound(result.Error.Message),
                "VALIDATION" => Results.BadRequest(result.Error.Message),
                "CONFLICT"   => Results.Conflict(result.Error.Message),
                _            => Results.Problem(result.Error.Message)
            };
}
