namespace Dewey.Web.Auth;

public sealed class CognitoOptions
{
    public string Region { get; set; } = "us-east-1";
    public string UserPoolClientId { get; set; } = "";
}
