using CloudNative.CloudEvents;
using Google.Cloud.Functions.Framework;
using Google.Events.Protobuf.Cloud.Storage.V1;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Storage.V1;
using Markdig;
using iText.Html2pdf;

namespace gcf_md2pdf;

/// <summary>
/// A function that can be triggered in responses to changes in Google Cloud Storage.
/// The type argument (StorageObjectData in this case) determines how the event payload is deserialized.
/// The function must be deployed so that the trigger matches the expected payload type. (For example,
/// deploying a function expecting a StorageObject payload will not work for a trigger that provides
/// a FirestoreEvent.)
/// </summary>
public class Function : ICloudEventFunction<StorageObjectData>
{
    /// <summary>
    /// Logic for your function goes here. Note that a CloudEvent function just consumes an event;
    /// it doesn't provide any response.
    /// </summary>
    /// <param name="cloudEvent">The CloudEvent your function should consume.</param>
    /// <param name="data">The deserialized data within the CloudEvent.</param>
    /// <param name="cancellationToken">A cancellation token that is notified if the request is aborted.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleAsync(CloudEvent cloudEvent, StorageObjectData data, CancellationToken cancellationToken)
    {
        Console.WriteLine("Storage object information:");
        Console.WriteLine($"  Name: {data.Name}");
        Console.WriteLine($"  Bucket: {data.Bucket}");
        Console.WriteLine($"  Size: {data.Size}");
        Console.WriteLine($"  Content type: {data.ContentType}");
        Console.WriteLine("CloudEvent information:");
        Console.WriteLine($"  ID: {cloudEvent.Id}");
        Console.WriteLine($"  Source: {cloudEvent.Source}");
        Console.WriteLine($"  Type: {cloudEvent.Type}");
        Console.WriteLine($"  Subject: {cloudEvent.Subject}");
        Console.WriteLine($"  DataSchema: {cloudEvent.DataSchema}");
        Console.WriteLine($"  DataContentType: {cloudEvent.DataContentType}");
        Console.WriteLine($"  Time: {cloudEvent.Time?.ToUniversalTime():yyyy-MM-dd'T'HH:mm:ss.fff'Z'}");
        Console.WriteLine($"  SpecVersion: {cloudEvent.SpecVersion}");

        // In this example, we don't need to perform any asynchronous operations, so the
        // method doesn't need to be declared async.

        if(cloudEvent.Type == "google.cloud.storage.object.v1.finalized")
        {
            Console.WriteLine("File caricato su storage");
            if(data.Name.EndsWith(".txt"))
            {
                await convertMd2PDFAsync(data);
                Console.WriteLine("Conversione in PDF completata con successo.");
            }
            else
            {
                Console.WriteLine("File non di tipo .txt, conversione in PDF non eseguita...");
            }
;
        }
        else
        {
            Console.WriteLine("File non caricato, conversione in PDF non eseguita...");

        }
    }

    private async Task convertMd2PDFAsync(StorageObjectData data)
    {
        Console.WriteLine("Inizio conversione in PDF...");
        //read value from environment variable or set default value
        string outputBucketPath = Environment.GetEnvironmentVariable("OutputBucketNamePDF") ?? "file-pdf-test";
        Console.WriteLine($"Output bucket name: {outputBucketPath}");

        string inputBucketPath = data.Bucket;


        // Initialize the client
        var storageClient = await StorageClient.CreateAsync();

        // Specify the local working directory for saving files
        string localTempPath = "./pdfs";
        //check if directory exists
        if (!Directory.Exists(localTempPath))
        {
            Console.WriteLine($"Directory {localTempPath} non esistente, creazione della directory...");
            Directory.CreateDirectory(localTempPath);
        }

        try
        {
            var storageObject = await storageClient.GetObjectAsync(data.Bucket, data.Name);


            // Get the object name
            string objectName = storageObject.Name;

            // Define file paths
            string fileName = System.IO.Path.GetFileNameWithoutExtension(objectName);
            string htmlFilePath = System.IO.Path.Combine(localTempPath, $"{fileName}.html");
            var pdfName = fileName.Split(".txt")[0] + ".pdf";
            string pdfFilePath = System.IO.Path.Combine(localTempPath, pdfName);



            // Download the file from Google Cloud Storage

            string currentFile = "";
            using (var outputFile = File.Create(System.IO.Path.Combine(localTempPath, fileName)))
            {
                await storageClient.DownloadObjectAsync(inputBucketPath, objectName, outputFile);
                Console.WriteLine($"File {objectName} downloaded to {outputFile.Name}");
                currentFile = outputFile.Name;
            }


            // Convert Markdown to HTML using Markdig
            string markdownContent = await File.ReadAllTextAsync(currentFile);
            var pipeline = new MarkdownPipelineBuilder().Build();
            string htmlContent = Markdig.Markdown.ToHtml(markdownContent, pipeline);
            await File.WriteAllTextAsync(htmlFilePath, htmlContent);
            Console.WriteLine($"HTML saved to {htmlFilePath}");

            // Aggiungi stili CSS
            htmlContent = AggiungiStiliCss(htmlContent);

            // Save the HTML content (optional)
            File.WriteAllText(htmlFilePath, htmlContent);

            // Save the HTML content (optional)
            File.WriteAllText(htmlFilePath, htmlContent);

            // Conversione da HTML a PDF usando iText
            using (FileStream pdfStream = new FileStream(pdfFilePath, FileMode.Create))
            {
                ConverterProperties props = new ConverterProperties();
                props.SetBaseUri(localTempPath); //Setting base uri

                HtmlConverter.ConvertToPdf(htmlContent, pdfStream, props);
            }

            Console.WriteLine($"PDF generato e salvato con successo in: {pdfFilePath}");


            // Upload the PDF to Google Cloud Storage
            using (var fileStream = File.OpenRead(pdfFilePath))
            {
                Console.WriteLine($"Uploading PDF to {outputBucketPath}/{pdfName}");
                await storageClient.UploadObjectAsync(outputBucketPath, pdfName, "application/pdf", fileStream);
                Console.WriteLine($"PDF uploaded to {outputBucketPath}/{pdfName}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing file {data.Name} in {data.Bucket} : {ex.Message}");
        }    

    }

    static string AggiungiStiliCss(string htmlContent)
    {
        // Aggiungi stili CSS per i margini
        string cssStili = @"
            <style>
                body {
                    margin: 50px 50px 50px 50px; /* Top, Right, Bottom, Left */
                }
            </style>";

        // Aggiungi il foglio di stile all'interno del tag <head>
        string htmlConStili = $@"
            <html>
            <head>
                {cssStili}
            </head>
            <body>
            {htmlContent}
            </body>
            </html>";

        return htmlConStili;
    }
}
