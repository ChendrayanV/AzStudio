# AzStudio

A Windows desktop GUI for connecting to Azure services from a Windows Server (or any Windows machine), currently supporting:

- **Azure Blob Storage** — browse containers/blobs, upload, download, delete.
- **Azure Service Bus (Queues and Topics)** — browse queues and topics/subscriptions side by side (selecting a queue or a subscription auto-peeks its messages), view message counts, peek messages (non-destructive), send test messages to a queue or a topic, or scan every queue and every topic/subscription in the namespace at once via **Service Bus → All Messages → Scan All Accessible Messages** — entities the signed-in identity isn't authorized for are skipped rather than failing the scan. Check **View dead-letter messages** to peek a queue's or subscription's dead-letter sub-queue instead of its main queue (the scan-all view includes dead-letter messages automatically, tagged "(DLQ)", for any entity that has some). Double-click any message row (in either view) to see its full details — content type, correlation/session/partition IDs, delivery count, TTL/expiry, application properties, and the untruncated body.

Two authentication modes are supported per saved connection:

- **Service Principal** — Tenant ID, Client (App) ID, Client secret.
- **Sign in as user (Azure AD)** — interactive browser sign-in (MSAL via `Azure.Identity`), with the signed-in token cached to disk so you aren't prompted every launch.

### Navigating: Connections vs. Azure Services

The left pane has two blocks. **Connections** (top) is where you pick/manage the saved identity and click **Connect**/**Disconnect**. **Azure Services** (below it, enabled once connected) is where you pick which service's panel shows on the right — **Storage** or **Service Bus**. Switching between them doesn't reconnect or lose state; each service panel keeps its own account/namespace field, lists, and last-loaded data independently.

### Connecting to a specific storage account / Service Bus namespace

A saved connection is just an identity (an auth mode + tenant, and optionally a default account/namespace). The account or namespace you actually browse is typed directly into the **Storage account** / **Namespace** box on each service panel and can be changed at any time without editing or re-creating the connection — click **Load Containers** / **Load Topics & Queues** after typing a name to (re)connect to it. Clicking **Connect** in the left panel only establishes the Azure AD identity; it also auto-loads whichever service had a default name saved on the profile.

### Why sign-in shouldn't ask twice

For **Sign in as user (Azure AD)**, clicking **Connect** performs one lightweight sign-in up front (`InteractiveBrowserCredential.AuthenticateAsync()` — establishing the account, not requesting any specific Azure resource's token) and persists the resulting MSAL `AuthenticationRecord` to `%APPDATA%\AzStudio\auth-records\{connectionId}.json`. That record — not a secret, just account/tenant identifiers — is what lets every later request, for Storage *or* Service Bus (they're separate token audiences), silently resume the same signed-in account instead of independently deciding it needs its own fresh interactive prompt. It's also reused on the next **Connect** for the same saved connection, including across app restarts, so in the normal case you sign in interactively once, ever, per connection.

If Connect still prompts you twice, or a specific service still fails to authenticate, check first whether the error is actually about authentication — Storage/Service Bus errors that look like a permissions problem are sometimes really a DNS/network reachability failure (e.g. "the requested name is valid but no data of the requested type was found" for `<account>.blob.core.windows.net` means the name doesn't resolve — often a private-endpoint account that needs to be reached over VPN/private DNS, or a typo'd account name), not a 401/403.

### Service Bus: connect directly, or browse

The Service Bus → Browse tab leads with **Connect directly to a queue or topic**: enter the namespace and a queue name (or a topic + subscription name), then Peek/Send right there — no listing of the namespace's contents involved. This is the primary, always-available way in, and it's the one to use if you only know the specific entity you have access to.

**Load Topics & Queues** (further down) is an optional convenience for browsing everything in the namespace, but it needs **namespace-wide "manage" rights** (e.g. *Azure Service Bus Data Owner*) — separate from, and stricter than, the RBAC that lets you send/peek on one specific entity (e.g. *Azure Service Bus Data Receiver* scoped to a single queue). So it's entirely normal and expected for it to come back with a 401/403 for a user who has full access to one particular queue or topic/subscription but no ability to enumerate the namespace — that's how Service Bus RBAC works, not a bug, and it isn't fixable from the client side; use direct connect instead. Listing queues and listing topics are also tried independently of each other when you do have some list access, so having permission to list one but not the other doesn't block the one you do have access to.

## Solution layout

```
AzStudio.sln
src/
  AzStudio.Core/     Auth, profile storage, and Azure service wrappers (no UI dependency)
  AzStudio.App/       WPF (.NET 8) desktop UI
```

`AzStudio.Core` is deliberately UI-agnostic so new Azure service modules can be added later without touching the WPF layer's plumbing:

- `Auth/CredentialFactory.cs` builds a `TokenCredential` from a `ConnectionProfile` — every service should authenticate through this.
- `Storage/BlobStorageService.cs` and `ServiceBus/ServiceBusService.cs` are thin wrappers around the Azure SDK clients. A new service (e.g. Cosmos DB, Key Vault) follows the same pattern: a `*Service` class in Core taking a `TokenCredential`, plus a `*TabViewModel` and a `TabItem` in `MainWindow.xaml`.
- `Profiles/ProfileStore.cs` persists connections to `%APPDATA%\AzStudio\profiles.json`. Client secrets are encrypted at rest with Windows DPAPI (`Security/SecretProtector.cs`), scoped to the signed-in Windows user — a copied profile file cannot be decrypted on another machine or by another account.

## Building

Requires the .NET 8 SDK.

```powershell
dotnet build AzStudio.sln
```

## Running from source

```powershell
dotnet run --project src/AzStudio.App/AzStudio.App.csproj
```

## Publishing a standalone executable for a Windows Server

This produces a single `.exe` with the .NET runtime bundled in, so no separate runtime install is needed on the target server:

```powershell
dotnet publish src/AzStudio.App/AzStudio.App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Copy `publish\AzStudio.exe` to the server and run it — no installer required.

## Notes on authentication

- **Service Principal**: needs an Azure AD app registration with a client secret, and appropriate RBAC roles on the target resources (e.g. *Storage Blob Data Contributor* on the storage account, *Azure Service Bus Data Owner*/*Receiver*/*Sender* on the namespace).
- **Interactive user sign-in**: uses `Azure.Identity`'s built-in developer sign-in app by default (no app registration required to get started). If your tenant restricts sign-in to specific registered apps, set a custom **Client (App) ID** on the connection (a public client / mobile & desktop app registration with the appropriate delegated API permissions).
- Both modes ultimately just produce a `TokenCredential`, so RBAC — not the sign-in method — is what determines what a connection can actually see or do against Blob Storage / Service Bus.

## Adding a new service module later

1. Add a `*Service` wrapper class under `AzStudio.Core` that takes a `TokenCredential` (see `BlobStorageService` for the shape).
2. Add a matching `*TabViewModel` under `AzStudio.App/ViewModels` with an `Activate(TokenCredential credential, string defaultTarget)` / `Deactivate()` pair and an `EnsureService()` helper that (re)builds the `*Service` from whatever name is currently typed into the tab, following `BlobStorageTabViewModel`. This is what lets the user type/change the target resource name after connecting instead of baking it into the saved profile.
   - **Important:** a `[RelayCommand]`'s enabled state only re-evaluates when explicitly told to, or when an `[ObservableProperty]` with `[NotifyCanExecuteChangedFor]` changes. Since `CanExecute` here depends on plain fields (`_credential`, `_service`) rather than observable properties, every place that mutates those fields (`Activate`, `Deactivate`, `EnsureService`) must call a `NotifyServiceCommands()` helper that invokes `.NotifyCanExecuteChanged()` on each affected command — otherwise the buttons stay stuck disabled even after a successful connect.
3. Wire it into `MainViewModel` (a new tab view-model property + `Activate`/`Deactivate` calls in `ConnectAsync`/`Disconnect`) and add a `TabItem` to `MainWindow.xaml`.
4. If the new service needs its own default target field (like `StorageAccountName`), add it to `ConnectionProfile` and to the connection editor dialog.
