using HttpServer.Caching;
using System.Net;
using System.Text;

namespace HttpServer;

internal class HttpServer
{

    public static readonly byte[] badRequestBody = Encoding.ASCII.GetBytes("<h1>Bad request.</h1>");
    public static readonly byte[] methodNotAllowedBody = Encoding.ASCII.GetBytes("<h1>Method not allowed.</h1>");
    public static readonly byte[] forbiddenRequestBody = Encoding.ASCII.GetBytes("<h1>Forbidden.</h1>");
    public static readonly byte[] notFoundRequestBody = Encoding.ASCII.GetBytes("<h1>Not found.</h1>");

    private string baseUrl;
    private int port;
    private string rootDirectoryPath;
    private ReaderWriterLRUCache<string, byte[]> cache;
    private string errLogFilePath;
    private object _logFileLock = new object();

    public HttpServer(string baseUrl, int port, string rootDirectoryPath, string errLogFilePath, int cacheCapacity)
    {
        this.baseUrl = baseUrl;
        this.port = port;
        this.rootDirectoryPath = rootDirectoryPath;
        this.errLogFilePath = errLogFilePath;
        cache = new ReaderWriterLRUCache<string, byte[]>(cacheCapacity);
    }

    public async Task Launch()
    {
        string address = $"{baseUrl}:{port}/";
        using (var listener = new HttpListener())
        {
            listener.Prefixes.Add(address);
            listener.Start();
            Console.WriteLine($"Listening on {address}...");
            while (listener.IsListening)
            {
                var context = await listener.GetContextAsync();
                Console.WriteLine($"Request received from {context.Request.RemoteEndPoint}.");
                Task.Run(() => ProcessRequest(context));
            }
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        try
        {

            #region Validation

            if (!context.Request.HttpMethod.Equals("GET"))
            {
                SendResponse(context, methodNotAllowedBody, "text/html", HttpStatusCode.MethodNotAllowed);
                return;
            }

            string? fileName = Path.GetFileName(context.Request.RawUrl);

            if (ReferenceEquals(fileName, null) || fileName.Equals(string.Empty))
            {
                SendResponse(context, badRequestBody, "text/html", HttpStatusCode.BadRequest);
                return;
            }

            string extension = Path.GetExtension(fileName);

            if (extension.Equals(string.Empty) || !extension.Equals(".gif"))
            {
                SendResponse(context, forbiddenRequestBody, "text/html", HttpStatusCode.Forbidden);
                return;
            }

            #endregion

            #region Caching

            byte[] responseBody;

            // ako je zahtev prosao validaciju, pokusaj da procitas fajl iz kes memorije
            if (cache.TryRead(fileName, out responseBody))
            {
                // ako je fajl procitan iz kes memorije, posalji ga klijentu i kesiraj
                SendResponse(context, responseBody, "image/gif");
                cache.Write(fileName, responseBody);
                return;
            }

            // u suprotnom, nadji putanju do fajla i procitaj ga sa diska ukoliko postoji
            // posalji ga klijentu a zatim ga dodaj u kes memoriju
            string? filePath = FileSystemUtil.SearchDirectoryForFile(rootDirectoryPath, fileName);
            if (filePath != null)
            {
                responseBody = File.ReadAllBytes(filePath);
                SendResponse(context, responseBody, "image/gif");
                cache.Write(fileName, responseBody);
                return;
            }

            // ukoliko fajl ne postoji, posalji 404
            SendResponse(context, notFoundRequestBody, "text/html", HttpStatusCode.NotFound);

            #endregion
        }
        catch (Exception ex)
        {
            if (context.Response.OutputStream.CanWrite)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.OutputStream.Close();
            }
            lock (_logFileLock)
            {
                File.AppendAllText(errLogFilePath, ex.Message + "\n");
            }
        }
    }

    private async Task SendResponse(HttpListenerContext context, byte[] responseBody, string contentType, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        // Formiramo string koji ce biti logovan na konzoli
        // pri handle-ovanju zahteva
        string logString = string.Format(
            "REQUEST:\n{0} {1} HTTP/{2}\nHost: {3}\nUser-agent: {4}\n-------------------\nRESPONSE:\nStatus: {5}\nDate: {6}\nContent-Type: {7}\nContent-Length: {8}\n",
            context.Request.HttpMethod,
            context.Request.RawUrl,
            context.Request.ProtocolVersion,
            context.Request.UserHostName,
            context.Request.UserAgent,
            statusCode,
            DateTime.Now,
            contentType,
            responseBody.Length
        );
        // Postavljamo parametre response-a i upisujemo podatke u body
        context.Response.ContentType = contentType;
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentLength64 = responseBody.Length;
        // Saljemo response i radimo cleanup objekta outputStream
        using (Stream outputStream = context.Response.OutputStream)
        {
            await outputStream.WriteAsync(responseBody, 0, responseBody.Length);
        }
        Console.WriteLine(logString);
    }
}
