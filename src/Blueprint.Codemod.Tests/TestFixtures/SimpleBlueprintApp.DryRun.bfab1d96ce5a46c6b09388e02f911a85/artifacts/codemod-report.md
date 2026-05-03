# migration-diagnostics

- Schema version: 1
- Findings: 5

## Buckets

- auto-fixable: 1
- configuration required: 2
- consumer-extension rewrite required: 2
- manual design review required: 0

## Findings

### BPMIG001
- Title: Remove redundant AddOpenApi activation
- Category: auto-fixable
- Severity: Info
- Documentation: migration\openapi\plan.md
- Message: Remove 'AddOpenApi()' because Blueprint HTTP hosts now expose OpenAPI by default.

### BPMIG100
- Title: Migrate configured AddOpenApi activation
- Category: configuration required
- Severity: Warning
- Documentation: migration\openapi\plan.md
- Message: Migrate configured 'AddOpenApi(...)' usage to the future 'ConfigureOpenApi(...)' host-level seam because OpenAPI is no longer activated through BlueprintApiBuilder.

### BPMIG101
- Title: Migrate configured AddAuthentication activation
- Category: configuration required
- Severity: Warning
- Documentation: migration\authentication-and-authorisation\plan.md
- Message: Migrate configured 'AddAuthentication(...)' usage through the new authentication configuration path because auth stages are no longer activated through BlueprintApiBuilder feature toggles.

### BPMIG200
- Title: Migrate FromResource usage
- Category: consumer-extension rewrite required
- Severity: Warning
- Documentation: migration\authentication-and-authorisation\plan.md
- Message: Migrate 'FromResource(...)' usage because auth/resource inheritance must move into Blueprint's source-generated metadata model.

### BPMIG201
- Title: Migrate auth placeholder usage
- Category: consumer-extension rewrite required
- Severity: Warning
- Documentation: migration\authentication-and-authorisation\plan.md
- Message: Migrate '{Auth:...}' placeholder usage because auth-template expansion must move into Blueprint's source-generated metadata model.
