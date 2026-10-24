namespace TickeTask.Web.Api;

public static partial class ApiEndpoints
{
    public static IEndpointRouteBuilder MapTickeTaskApi(this IEndpointRouteBuilder app)
    {
        MapSession(app);
        MapUsers(app);
        MapConfig(app);
        MapProject(app);
        MapCustomers(app);
        MapPeople(app);
        MapDevices(app);
        MapMessages(app);
        MapTickets(app);
        MapAnalyze(app);
        MapSettings(app);
        MapMail(app);
        MapModel(app);
        MapSetup(app);
        MapWebhooks(app);
        return app;
    }
}
