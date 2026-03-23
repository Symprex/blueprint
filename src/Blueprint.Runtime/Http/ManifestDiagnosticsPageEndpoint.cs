using System.Text;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Builds the development-only diagnostics page shell that treats the hosted canonical JSON manifest as the only
/// runtime input, so richer inspection can grow without teaching <c>Blueprint.Runtime</c> how to interpret the
/// manifest schema itself.
/// </summary>
internal static class ManifestDiagnosticsPageEndpoint
{
    private static readonly string Html = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Blueprint manifest diagnostics</title>
          <style>
            :root {
              color-scheme: light dark;
              font-family: "Segoe UI", Arial, sans-serif;
            }

            body {
              margin: 0;
              background: Canvas;
              color: CanvasText;
            }

            main {
              max-width: 1100px;
              margin: 0 auto;
              padding: 2rem 1.5rem 3rem;
            }

            h1,
            h2 {
              margin-bottom: 0.5rem;
            }

            p {
              line-height: 1.5;
            }

            .muted {
              color: GrayText;
            }

            .status {
              padding: 0.75rem 1rem;
              border-radius: 0.5rem;
              background: color-mix(in srgb, CanvasText 8%, Canvas 92%);
            }

            .status.error {
              background: color-mix(in srgb, red 15%, Canvas 85%);
            }

            .summary-grid {
              display: grid;
              gap: 0.75rem;
              grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
              margin: 1.5rem 0;
            }

            .summary-card {
              padding: 1rem;
              border: 1px solid color-mix(in srgb, CanvasText 14%, Canvas 86%);
              border-radius: 0.75rem;
              background: color-mix(in srgb, CanvasText 4%, Canvas 96%);
            }

            .summary-card dt {
              font-size: 0.85rem;
              color: GrayText;
              margin-bottom: 0.35rem;
            }

            .summary-card dd {
              margin: 0;
              font-weight: 600;
              overflow-wrap: anywhere;
            }

            table {
              width: 100%;
              border-collapse: collapse;
              margin-top: 1rem;
            }

            th,
            td {
              padding: 0.75rem;
              text-align: left;
              border-bottom: 1px solid color-mix(in srgb, CanvasText 12%, Canvas 88%);
              vertical-align: top;
            }

            code {
              font-family: Consolas, "Courier New", monospace;
            }
          </style>
        </head>
        <body>
          <main>
            <h1>Blueprint manifest diagnostics</h1>
            <p>
              This development-only page fetches the canonical hosted manifest JSON from
              <a href="operations.json"><code>operations.json</code></a> and renders a lightweight inspection view
              without introducing a second runtime manifest model.
            </p>
            <p class="status" id="status">Loading canonical manifest...</p>

            <section aria-labelledby="summary-heading">
              <h2 id="summary-heading">Summary</h2>
              <dl class="summary-grid">
                <div class="summary-card">
                  <dt>App type</dt>
                  <dd id="app-type">-</dd>
                </div>
                <div class="summary-card">
                  <dt>App assembly</dt>
                  <dd id="app-assembly">-</dd>
                </div>
                <div class="summary-card">
                  <dt>Generator</dt>
                  <dd id="generator">-</dd>
                </div>
                <div class="summary-card">
                  <dt>Operations</dt>
                  <dd id="operation-count">-</dd>
                </div>
              </dl>
            </section>

            <section aria-labelledby="operations-heading">
              <h2 id="operations-heading">Operations</h2>
              <table>
                <thead>
                  <tr>
                    <th scope="col">Operation</th>
                    <th scope="col">Kind</th>
                    <th scope="col">Namespace</th>
                    <th scope="col">Type name</th>
                  </tr>
                </thead>
                <tbody id="operations-body">
                  <tr>
                    <td colspan="4" class="muted">Waiting for manifest data...</td>
                  </tr>
                </tbody>
              </table>
            </section>
          </main>

          <script>
            const statusElement = document.getElementById("status");
            const operationsBody = document.getElementById("operations-body");

            function setText(id, value) {
              document.getElementById(id).textContent = value ?? "-";
            }

            function appendCell(row, value) {
              const cell = document.createElement("td");
              cell.textContent = value ?? "-";
              row.appendChild(cell);
            }

            function renderOperations(operations) {
              operationsBody.replaceChildren();

              if (operations.length === 0) {
                const row = document.createElement("tr");
                const cell = document.createElement("td");
                cell.colSpan = 4;
                cell.className = "muted";
                cell.textContent = "No operations were published in the manifest.";
                row.appendChild(cell);
                operationsBody.appendChild(row);
                return;
              }

              for (const [operationName, operation] of operations) {
                const row = document.createElement("tr");
                appendCell(row, operationName);
                appendCell(row, operation.kind);
                appendCell(row, operation.namespace);
                appendCell(row, operation.typeName);
                operationsBody.appendChild(row);
              }
            }

            async function loadManifest() {
              const response = await fetch("operations.json", {
                headers: {
                  "Accept": "application/json"
                }
              });

              if (!response.ok) {
                throw new Error(`Manifest request failed with status ${response.status}.`);
              }

              const manifest = await response.json();
              const operations = Object.entries(manifest.operations ?? {});

              setText("app-type", manifest.app?.typeName);
              setText("app-assembly", manifest.app?.assemblyName);
              setText("generator", [manifest.generator?.productName, manifest.generator?.version].filter(Boolean).join(" "));
              setText("operation-count", String(operations.length));
              renderOperations(operations);

              statusElement.textContent = `Loaded ${operations.length} operations from canonical JSON.`;
              statusElement.classList.remove("error");
            }

            loadManifest().catch(error => {
              console.error("Manifest load error:", error);
              statusElement.textContent = "Unable to load canonical manifest. Check browser console for details.";
              statusElement.classList.add("error");
            });
          </script>
        </body>
        </html>
        """;

    /// <summary>
    /// Creates the request delegate that serves the static diagnostics page shell.
    /// </summary>
    /// <returns>A request delegate that returns the hosted manifest diagnostics page.</returns>
    public static RequestDelegate Create()
    {
        return async httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/html; charset=utf-8";
            await httpContext.Response.WriteAsync(Html, Encoding.UTF8, httpContext.RequestAborted);
        };
    }
}
