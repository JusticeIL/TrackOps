using Microsoft.AspNetCore.Mvc.Routing;

namespace TrackOps.Api.Http;

/// <summary>
/// Maps an action to the HTTP QUERY method (RFC 10008), the way [HttpGet] maps one to GET.
/// .NET 11 knows the method (HttpMethods.Query, HttpMethods.IsQuery) but ships no attribute for
/// it, so this one is built on the framework's own constant.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HttpQueryAttribute : HttpMethodAttribute
{
	public HttpQueryAttribute()
		: base(new[] { Microsoft.AspNetCore.Http.HttpMethods.Query })
	{
	}

	public HttpQueryAttribute(string i_Template)
		: base(new[] { Microsoft.AspNetCore.Http.HttpMethods.Query }, i_Template)
	{
	}
}
