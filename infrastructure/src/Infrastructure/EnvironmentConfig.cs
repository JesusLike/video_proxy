namespace Infrastructure;

public class EnvironmentConfig
{
    public required string EnvironmentName { get; init; }
    public required string Account { get; init; }
    public required string Region { get; init; }
    public required string DomainName { get; init; }

    public Amazon.CDK.Environment ToAwsEnvironment() => new()
    {
        Account = Account,
        Region = Region
    };

    public static EnvironmentConfig FromContext(Amazon.CDK.App app, string envName) => new()
    {
        EnvironmentName = envName,
        Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT")
            ?? throw new InvalidOperationException("CDK_DEFAULT_ACCOUNT environment variable is not set"),
        Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION") ?? "us-east-1",
        DomainName = app.Node.TryGetContext("domain")?.ToString()
            ?? throw new InvalidOperationException("Pass --context domain=yourdomain.com")
    };
}
