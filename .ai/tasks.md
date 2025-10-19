{"id": 1, "task": "Review and finalize plan.md with stakeholder feedback", "area": "Planning", "status": "done", "dependencies": []}
{"id": 2, "task": "Create data models for handler discovery (OperationHandlerModel, HandlerParameter, enums)", "area": "Models", "status": "done", "dependencies": [1]}
{"id": 3, "task": "Implement OperationHandlerModel with equality comparison for incremental generation", "area": "Models", "status": "done", "dependencies": [2]}
{"id": 4, "task": "Create diagnostic descriptors for all error and warning cases (BP100-BP103, BP200-BP201)", "area": "Diagnostics", "status": "done", "dependencies": [1]}
{"id": 5, "task": "Implement FindInlineHandler method to discover handler methods on operation classes", "area": "Discovery", "status": "done", "dependencies": [3, 4]}
{"id": 6, "task": "Implement FindExternalHandler method to search for handlers in separate classes", "area": "Discovery", "status": "done", "dependencies": [3, 4]}
{"id": 7, "task": "Implement AnalyzeParameters method to determine parameter sources", "area": "Discovery", "status": "done", "dependencies": [3]}
{"id": 8, "task": "Implement DetermineParameterSource with resolution rules (Operation, Context, ContextProperty, DI)", "area": "Discovery", "status": "done", "dependencies": [7]}
{"id": 9, "task": "Create OperationExecutorPipelineSourceGenerator with incremental pipeline stages", "area": "Generator", "status": "done", "dependencies": [3]}
{"id": 10, "task": "Implement Stage 1: Find BlueprintApp and extract operation registrations", "area": "Generator", "status": "planned", "dependencies": [9]}
{"id": 11, "task": "Implement Stage 2: For each operation, discover handler using FindInlineHandler and FindExternalHandler", "area": "Generator", "status": "planned", "dependencies": [5, 6, 10]}
{"id": 12, "task": "Implement Stage 3: Generate pipeline class per operation with proper structure", "area": "CodeGen", "status": "planned", "dependencies": [11]}
{"id": 13, "task": "Generate pipeline constructor with ILoggerFactory and required dependencies", "area": "CodeGen", "status": "planned", "dependencies": [12]}
{"id": 14, "task": "Generate LoggerMessage.Define static fields for structured logging", "area": "CodeGen", "status": "planned", "dependencies": [12]}
{"id": 15, "task": "Generate ExecuteAsync method with activity tracing setup", "area": "CodeGen", "status": "planned", "dependencies": [12]}
{"id": 16, "task": "Generate handler parameter resolution code (operation, context, DI)", "area": "CodeGen", "status": "planned", "dependencies": [8, 15]}
{"id": 17, "task": "Generate handler invocation code for inline handlers (static and instance)", "area": "CodeGen", "status": "planned", "dependencies": [16]}
{"id": 18, "task": "Generate handler invocation code for external handlers with ActivatorUtilities fallback", "area": "CodeGen", "status": "planned", "dependencies": [16]}
{"id": 19, "task": "Implement compile-time base class property analysis with caching per type", "area": "CodeGen", "status": "planned", "dependencies": [18]}
{"id": 20, "task": "Generate base class property setup for external handlers (Context, Logger, etc.)", "area": "CodeGen", "status": "planned", "dependencies": [19]}
{"id": 21, "task": "Generate exception handling (ValidationException, ApiException, unhandled)", "area": "CodeGen", "status": "planned", "dependencies": [15]}
{"id": 22, "task": "Generate activity tagging for telemetry (status codes, error descriptions)", "area": "CodeGen", "status": "planned", "dependencies": [21]}
{"id": 23, "task": "Add behaviour injection point comments in generated code", "area": "CodeGen", "status": "planned", "dependencies": [15]}
{"id": 24, "task": "Implement Stage 4: Generate BlueprintAppExecutor with all pipeline registrations", "area": "CodeGen", "status": "planned", "dependencies": [12]}
{"id": 25, "task": "Generate executor constructor with all pipeline dependencies", "area": "CodeGen", "status": "planned", "dependencies": [24]}
{"id": 26, "task": "Generate Register calls for all discovered operation-pipeline pairs", "area": "CodeGen", "status": "done", "dependencies": [25]}
{"id": 28, "task": "Write unit tests for FindInlineHandler (static, instance, async, sync)", "area": "Tests", "status": "done", "dependencies": [5]}
{"id": 29, "task": "Write unit tests for FindExternalHandler", "area": "Tests", "status": "done", "dependencies": [6]}
{"id": 29, "task": "Write unit tests for AnalyzeParameters with all parameter sources", "area": "Tests", "status": "planned", "dependencies": [7, 8]}
{"id": 30, "task": "Write unit tests for diagnostic emission (no handler, multiple handlers)", "area": "Tests", "status": "planned", "dependencies": [4, 11]}
{"id": 31, "task": "Write snapshot tests for inline handler pipeline generation", "area": "Tests", "status": "planned", "dependencies": [17, 20, 21]}
{"id": 32, "task": "Write snapshot tests for external handler pipeline generation", "area": "Tests", "status": "planned", "dependencies": [18, 19, 20, 21]}
{"id": 33, "task": "Write snapshot tests for executor generation", "area": "Tests", "status": "planned", "dependencies": [25]}
{"id": 34, "task": "Write integration test: compile sample with inline query handler", "area": "Tests", "status": "planned", "dependencies": [30]}
{"id": 35, "task": "Write integration test: compile sample with external command handler", "area": "Tests", "status": "planned", "dependencies": [31]}
{"id": 36, "task": "Write integration test: execute operation through generated pipeline", "area": "Tests", "status": "planned", "dependencies": [33, 34]}
{"id": 37, "task": "Update sample project to use generated executor instead of manual DelegatePipeline", "area": "Sample", "status": "planned", "dependencies": [35]}
{"id": 38, "task": "Add example inline handler to sample project", "area": "Sample", "status": "planned", "dependencies": [36]}
{"id": 39, "task": "Add example external handler to sample project", "area": "Sample", "status": "planned", "dependencies": [36]}
{"id": 40, "task": "Add example handler with multiple DI dependencies to sample project", "area": "Sample", "status": "planned", "dependencies": [36]}
{"id": 41, "task": "Update README.md with handler discovery overview", "area": "Docs", "status": "planned", "dependencies": [35]}
{"id": 42, "task": "Document inline vs external handler patterns", "area": "Docs", "status": "planned", "dependencies": [40]}
{"id": 43, "task": "Document parameter resolution rules and precedence", "area": "Docs", "status": "planned", "dependencies": [40]}
{"id": 44, "task": "Create troubleshooting guide for common issues", "area": "Docs", "status": "planned", "dependencies": [40]}
{"id": 45, "task": "Create migration guide from DelegatePipeline to generated pipelines", "area": "Docs", "status": "planned", "dependencies": [40]}
{"id": 46, "task": "Add XML comments to all public APIs in generator", "area": "Docs", "status": "planned", "dependencies": [9]}
{"id": 47, "task": "Run full test suite and verify >90% coverage", "area": "Validation", "status": "planned", "dependencies": [32, 35]}
{"id": 48, "task": "Build sample project and verify no warnings", "area": "Validation", "status": "planned", "dependencies": [39]}
{"id": 49, "task": "Run generator performance test (<5s for large project)", "area": "Validation", "status": "planned", "dependencies": [35]}
{"id": 50, "task": "Review generated code follows repository style guidelines", "area": "Validation", "status": "planned", "dependencies": [32]}
{"id": 51, "task": "Final review and prepare for merge", "area": "Review", "status": "planned", "dependencies": [46, 47, 48, 49]}
