using Amazon.CDK;
using Amazon.CDK.Assertions;
using Infrastructure;
using Infrastructure.Stacks;
using Xunit;

namespace Infrastructure.Tests;

public class EcrStackTest
{
    private readonly Template _template;

    public EcrStackTest()
    {
        var app = new App();
        var config = new EnvironmentConfig
        {
            EnvironmentName = "testing",
            Account = "123456789012",
            Region = "us-east-1",
            DomainName = "example.com"
        };
        var stack = new EcrStack(app, "test-ecr", new EcrStackProps { EnvConfig = config });
        _template = Template.FromStack(stack);
    }

    [Fact]
    public void HasThreeRepositories()
    {
        _template.ResourceCountIs("AWS::ECR::Repository", 3);
    }

    [Fact]
    public void AllRepositoriesHaveImageScanOnPush()
    {
        _template.HasResourceProperties("AWS::ECR::Repository", Match.ObjectLike(
            new Dictionary<string, object>
            {
                ["ImageScanningConfiguration"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["ScanOnPush"] = true
                })
            }));
    }

    [Fact]
    public void RepositoryNamesAreEnvironmentScoped()
    {
        _template.HasResourceProperties("AWS::ECR::Repository", Match.ObjectLike(
            new Dictionary<string, object> { ["RepositoryName"] = "testing/user-service" }));

        _template.HasResourceProperties("AWS::ECR::Repository", Match.ObjectLike(
            new Dictionary<string, object> { ["RepositoryName"] = "testing/messaging-service" }));

        _template.HasResourceProperties("AWS::ECR::Repository", Match.ObjectLike(
            new Dictionary<string, object> { ["RepositoryName"] = "testing/youtube-service" }));
    }
}
