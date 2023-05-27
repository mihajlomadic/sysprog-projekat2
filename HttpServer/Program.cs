namespace HttpServer;

internal class Program
{
    static async Task Main(string[] args)
    {
        string localhost = "http://127.0.0.1";
        int port = 8080;
        string rootDirectoryPath = @"../../../root/";
        HttpServer server = new HttpServer(localhost, port, rootDirectoryPath, @"../../../errLogFile.txt", 10);
        await server.Launch();
    }
}