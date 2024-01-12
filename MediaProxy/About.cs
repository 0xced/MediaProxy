using Microsoft.Azure.Functions.Worker;

namespace MediaProxy;

public class About
{
    [Function("about")]
    public void Run([HttpTrigger(AuthorizationLevel.Anonymous, "get")] FunctionContext context)
    {
    }
}