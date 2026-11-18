# IFCtoRDF

IFCtoRDF is a Windows app that turns building model files (`.ifc`) into `.ttl` files, a format for sharing linked building data. It can also combine and enrich `.ttl` files with AI suggestions for building elements, materials, construction tasks, work durations, and related emissions.

## How to use

For the ready-to-run app, extract `IFCtoRDF_app.zip` and follow the included `DistributionREADME.txt`. In the app, choose the model file and where to save the `.ttl` result. AI features need a working AI provider and, for online providers, an API key.

## How the code is organized

- `UIs` contains the app window and its controls.
- `Converters` reads IFC files and turns building data into `.ttl` data.
- `Classes` holds the main building and graph data types.
- `Utils` contains supporting tools, including AI enrichment and translation.

The solution and project files are `IFCtoRDF.slnx` and `IFCtoRDF/IFCtoRDF.csproj`.
