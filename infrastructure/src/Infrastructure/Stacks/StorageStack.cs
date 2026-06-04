using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SecretsManager;
using Constructs;

namespace Infrastructure.Stacks;

public class StorageStackProps : StackProps
{
    public required EnvironmentConfig EnvConfig { get; init; }
    public required Vpc Vpc { get; init; }
    public required SecurityGroup DbSg { get; init; }
}

public class StorageStack : Stack
{
    public DatabaseInstance Database { get; }
    public Bucket VideoBucket { get; }
    public Bucket AppBucket { get; }
    public ISecret DbSecret { get; }

    public StorageStack(Construct scope, string id, StorageStackProps props) : base(scope, id, props)
    {
        var isProduction = props.EnvConfig.EnvironmentName == "production";

        DbSecret = new Secret(this, "DbSecret", new SecretProps
        {
            SecretName = $"{props.EnvConfig.EnvironmentName}/db-credentials",
            GenerateSecretString = new SecretStringGenerator
            {
                SecretStringTemplate = "{\"username\":\"postgres\"}",
                GenerateStringKey = "password",
                ExcludeCharacters = "/@\" "
            }
        });

        Database = new DatabaseInstance(this, "Database", new DatabaseInstanceProps
        {
            Engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps
            {
                Version = PostgresEngineVersion.VER_18
            }),
            InstanceType = InstanceType.Of(InstanceClass.T3, InstanceSize.MICRO),
            Vpc = props.Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
            SecurityGroups = new[] { props.DbSg },
            Credentials = Credentials.FromSecret(DbSecret),
            MultiAz = false,
            StorageEncrypted = true,
            BackupRetention = Duration.Days(7),
            DeleteAutomatedBackups = !isProduction,
            DeletionProtection = isProduction,
            RemovalPolicy = isProduction ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY
        });

        VideoBucket = new Bucket(this, "VideoBucket", new BucketProps
        {
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            EnforceSSL = true,
            LifecycleRules = new[]
            {
                new LifecycleRule { Expiration = Duration.Days(7), Enabled = true }
            },
            RemovalPolicy = RemovalPolicy.RETAIN
        });

        AppBucket = new Bucket(this, "AppBucket", new BucketProps
        {
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            EnforceSSL = true,
            RemovalPolicy = RemovalPolicy.RETAIN
        });

        Tags.Of(this).Add("Environment", props.EnvConfig.EnvironmentName);
    }
}
