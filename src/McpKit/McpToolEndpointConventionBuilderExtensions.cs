using Microsoft.AspNetCore.Builder;

namespace McpKit;

public static class McpToolEndpointConventionBuilderExtensions
{
    /// <summary>Publishes this GET endpoint as a read-only tool. Others are never exposed.</summary>
    public static TBuilder AsMcpTool<TBuilder>(this TBuilder builder, string? name = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new McpToolMetadata(name));
    }

    /// <summary>Publishes this endpoint as a tool that changes state.</summary>
    public static TBuilder AsMcpWriteTool<TBuilder>(this TBuilder builder, string? name = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new McpToolMetadata(name, changesState: true));
    }

    /// <summary>Makes the tool answer its first call with a preview and a token, and run only when called again with it.</summary>
    public static TBuilder RequireMcpConfirmation<TBuilder>(this TBuilder builder, TimeSpan? lifetime = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new McpToolConfirmationMetadata(lifetime));
    }

    /// <summary>Limits how often each client may call this tool, instead of the host's limit.</summary>
    public static TBuilder WithMcpRateLimit<TBuilder>(this TBuilder builder, int permitLimit, TimeSpan window)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new ToolRateLimit(permitLimit, window));
    }

    /// <summary>Masks fields in this tool's output on top of the host's rules.</summary>
    public static TBuilder WithMcpRedaction<TBuilder>(this TBuilder builder, Action<ToolRedaction> configure)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var redaction = new ToolRedaction();
        configure(redaction);
        return builder.WithMetadata(redaction);
    }

    /// <summary>Adds an optional tool argument that the endpoint gets as a request header.</summary>
    public static TBuilder WithMcpToolHeader<TBuilder>(this TBuilder builder, string header, string argument, string description)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new McpToolHeaderMetadata(header, argument, description));
    }
}
