using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Notes.Features.ZippedFolders;

namespace Notes.Tests.Integration;

/// <summary>
/// Encrypted archived folders (<c>"archive": { "enabled": true, "encrypted": true }</c>): their archive is encrypted with the folder's password,
/// which the app only knows in memory, from when someone unlocks the folder until it is zipped again. Each test has an app of its own.
/// </summary>
public sealed class ArchiveEncryptionTests() : PasswordTest(new NotesAppFactory()), IAsyncDisposable
{
    private const string Secret = "geheim 🔑";

    public async ValueTask DisposeAsync() => await App.DisposeAsync();

    private ArchivedFolders Archives => App.Services.GetRequiredService<ArchivedFolders>();
    private ArchivePasswords Passwords => App.Services.GetRequiredService<ArchivePasswords>();

    private string Vault => Tree.Physical("docs/vault");
    private string ArchivePath => Path.Combine(Vault, "_.7z");

    /// <summary>The configuration of docs/vault: archived (encrypted or not), with the password typed or as its hash.</summary>
    private static string VaultConfig(bool encrypted, string? typedPassword = null, string? hash = null, bool enabled = true)
    {
        JsonObject config = JsonNode.Parse(Cfg.Folder(Cfg.Perms()))!.AsObject();
        if (typedPassword is not null)
            config["password"] = typedPassword;
        if (hash is not null)
            config["passwordHash"] = hash;
        config["archive"] = new JsonObject { ["enabled"] = enabled, ["encrypted"] = encrypted };
        return config.ToJsonString();
    }

    private string HashOfVault() => JsonNode.Parse(File.ReadAllText(Path.Combine(Vault, ".config.json")))!["passwordHash"]!.GetValue<string>();

    /// <summary>docs/vault with two files, made an encrypted archived folder by an admin who types its password (and so has it unlocked).</summary>
    private async Task EncryptedVaultAsync()
    {
        Tree.File("docs/vault/note.md", "inside");
        Tree.File("docs/vault/deeper/more.md", "more");
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Admin, "/docs/vault?config", VaultConfig(true, Secret)));
    }

    /// <summary>Whether the archive can be read without the password, names included.</summary>
    private bool NamesAreReadable()
    {
        try
        {
            using FileStream file = File.OpenRead(ArchivePath);
            using SharpCompress.Archives.IArchive archive = SharpCompress.Archives.SevenZip.SevenZipArchive.OpenArchive(file,
                new SharpCompress.Readers.ReaderOptions());
            return archive.Entries.Any(entry => entry.Key == "note.md" && !entry.IsEncrypted);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Saves docs/vault's configuration as an admin who has it unlocked (the editor is behind its password, as everything in it).</summary>
    private async Task<HttpResponseMessage> SaveUnlockedAsync(string config, string password = Secret)
    {
        (_, string? cookie) = await UnlockAsync(Users.Admin, "/docs/vault", password);
        AntiforgeryTokens tokens = App.TokensFor(Users.Admin);
        return await Client.SendAsync(Users.Admin, HttpMethod.Post, "/docs/vault?config", new StringContent(config), antiforgery: false,
            headers: new Dictionary<string, string> { ["Cookie"] = cookie + "; " + tokens.Cookie, [tokens.HeaderName] = tokens.RequestToken });
    }

    /// <summary>What the file holds, read with the password in the header, waiting while it is unzipped (a slow key derivation, for a 7z).</summary>
    private async Task<HttpResponseMessage> ReadWithPasswordAsync(string address, string password)
    {
        for (int tries = 0; ; tries++)
        {
            HttpResponseMessage response = await GetWithPasswordAsync(Users.Alice, address + "?download", password);
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable || tries >= 50)
                return response;
            await Task.Delay(200, Cancellation);
        }
    }

    private static async Task Eventually(Func<bool> condition, string what)
    {
        DateTime end = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > end)
                Assert.Fail("Timed out waiting until " + what);
            await Task.Delay(20, Cancellation);
        }
    }

    [Fact]
    public async Task Its_archive_is_encrypted_names_included_and_the_password_opens_it_again()
    {
        await EncryptedVaultAsync();

        Assert.True(await Archives.ZipAsync(Vault));

        Assert.False(NamesAreReadable());
        // Forgotten once nothing needs it: a program that brings it along gets the folder unzipped with it
        Assert.False(Passwords.Knows(Vault));
        HttpResponseMessage read = await ReadWithPasswordAsync("/docs/vault/note.md", Secret);
        await AssertStatus(HttpStatusCode.OK, read);
        Assert.Equal("inside", await BodyOf(read));
        Assert.NotNull(Archives.MountedAt(Vault));
    }

    [Fact]
    public async Task Without_the_password_it_is_not_zipped_and_stays_unzipped()
    {
        await EncryptedVaultAsync();
        Passwords.Forget(Vault);

        Assert.False(await Archives.ZipAsync(Vault));

        Assert.NotNull(Archives.MountedAt(Vault));
        Assert.False(File.Exists(ArchivePath));
    }

    [Fact]
    public async Task Unlocked_but_the_app_forgot_the_password_asks_for_it_again()
    {
        await EncryptedVaultAsync();
        Assert.True(await Archives.ZipAsync(Vault));
        (HttpResponseMessage unlocked, string? cookie) = await UnlockAsync(Users.Alice, "/docs/vault", Secret);
        await AssertStatus(HttpStatusCode.NoContent, unlocked);
        Passwords.Forget(Vault);

        HttpResponseMessage asked = await GetWithCookieAsync(Users.Alice, "/docs/vault/note.md?download", cookie!);

        await AssertPasswordRequired("/docs/vault", asked);
        Assert.Null(Archives.MountedAt(Vault));
        await AssertStatus(HttpStatusCode.NoContent, (await UnlockAsync(Users.Alice, "/docs/vault", Secret)).Response);
        Assert.Equal("inside", await BodyOf(await ReadWithPasswordAsync("/docs/vault/note.md", Secret)));
    }

    [Fact]
    public async Task Encryption_needs_a_password_of_the_folder()
    {
        Tree.File("docs/vault/note.md", "inside");

        HttpResponseMessage response = await Client.PostAsync(Users.Admin, "/docs/vault?config", VaultConfig(true));

        await AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.Contains("archive.encrypted", await BodyOf(response));
    }

    [Fact]
    public async Task Turning_encryption_off_needs_the_right_password_typed_and_keeps_its_hash()
    {
        await EncryptedVaultAsync();
        Assert.True(await Archives.ZipAsync(Vault));
        string hash = HashOfVault();

        await AssertStatus(HttpStatusCode.BadRequest,
            await SaveUnlockedAsync(VaultConfig(false, hash: hash)));
        await AssertStatus(HttpStatusCode.BadRequest,
            await SaveUnlockedAsync(VaultConfig(false, typedPassword: "wrong")));
        Assert.False(NamesAreReadable());

        await AssertStatus(HttpStatusCode.OK, await SaveUnlockedAsync(VaultConfig(false, typedPassword: Secret)));

        Assert.Equal(hash, HashOfVault());
        Assert.NotNull(Archives.MountedAt(Vault));
        Assert.True(await Archives.ZipAsync(Vault));
        Assert.True(NamesAreReadable());
    }

    [Fact]
    public async Task Turning_encryption_on_encrypts_a_zipped_archive_right_away()
    {
        Tree.File("docs/vault/note.md", "inside");
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Admin, "/docs/vault?config", VaultConfig(false, Secret)));
        Assert.True(await Archives.ZipAsync(Vault));
        Assert.True(NamesAreReadable());

        HttpResponseMessage response = await SaveUnlockedAsync(VaultConfig(true, Secret));

        await AssertStatus(HttpStatusCode.OK, response);
        await Eventually(() => File.Exists(ArchivePath) && Archives.MountedAt(Vault) is null && Archives.ZipProgressOf(Vault) is null
                               && !NamesAreReadable(), "the archive is encrypted");
    }

    [Fact]
    public async Task A_new_password_encrypts_it_with_that_one_from_then_on()
    {
        await EncryptedVaultAsync();
        Assert.True(await Archives.ZipAsync(Vault));
        // (Changing the password needs it unlocked, which tells the app the old one)
        (_, string? cookie) = await UnlockAsync(Users.Admin, "/docs/vault", Secret);
        AntiforgeryTokens tokens = App.TokensFor(Users.Admin);
        HttpResponseMessage changed = await Client.SendAsync(Users.Admin, HttpMethod.Post, "/docs/vault?config",
            new StringContent(VaultConfig(true, "the new one")), antiforgery: false,
            headers: new Dictionary<string, string> { ["Cookie"] = cookie + "; " + tokens.Cookie, [tokens.HeaderName] = tokens.RequestToken });
        await AssertStatus(HttpStatusCode.OK, changed);
        // Unzipped with the old one before it was saved
        Assert.NotNull(Archives.MountedAt(Vault));

        Assert.True(await Archives.ZipAsync(Vault));

        await AssertStatus(HttpStatusCode.Forbidden, await ReadWithPasswordAsync("/docs/vault/note.md", Secret));
        Assert.Equal("inside", await BodyOf(await ReadWithPasswordAsync("/docs/vault/note.md", "the new one")));
    }

    [Fact]
    public async Task The_lock_button_zips_the_encrypted_folders_it_locks()
    {
        await EncryptedVaultAsync();
        Assert.True(await Archives.ZipAsync(Vault));
        (_, string? cookie) = await UnlockAsync(Users.Alice, "/docs/vault", Secret);
        HttpResponseMessage opened;
        while ((opened = await GetWithCookieAsync(Users.Alice, "/docs/vault/note.md?download", cookie!)).StatusCode == HttpStatusCode.ServiceUnavailable)
            await Task.Delay(200, Cancellation);
        await AssertStatus(HttpStatusCode.OK, opened);
        Assert.NotNull(Archives.MountedAt(Vault));

        await AssertStatus(HttpStatusCode.NoContent, await PostWithCookieAsync(Users.Alice, "/docs?lock", cookie!));

        await Eventually(() => Archives.MountedAt(Vault) is null && Archives.ZipProgressOf(Vault) is null && File.Exists(ArchivePath),
            "it is zipped");
        Assert.False(NamesAreReadable());
    }

    [Fact]
    public async Task With_onLowestLevel_the_folders_it_archives_are_encrypted_with_its_password()
    {
        Tree.File("docs/media/a/x.md", "x");
        JsonObject config = JsonNode.Parse(Cfg.Folder(Cfg.Perms()))!.AsObject();
        config["password"] = Secret;
        config["archive"] = new JsonObject { ["enabled"] = true, ["encrypted"] = true, ["onLowestLevel"] = true };
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Admin, "/docs/media?config", config.ToJsonString()));
        string lowest = Tree.Physical("docs/media/a");
        Assert.NotNull(Archives.MountedAt(lowest));

        Assert.True(await Archives.ZipAsync(lowest));

        Assert.False(Passwords.Knows(Tree.Physical("docs/media")));
        await AssertStatus(HttpStatusCode.Forbidden, await ReadWithPasswordAsync("/docs/media/a/x.md", "wrong"));
        HttpResponseMessage read = await ReadWithPasswordAsync("/docs/media/a/x.md", Secret);
        await AssertStatus(HttpStatusCode.OK, read);
        Assert.Equal("x", await BodyOf(read));
    }

    [Fact]
    public async Task The_listing_shows_both_its_password_and_its_zipper()
    {
        await EncryptedVaultAsync();
        Assert.True(await Archives.ZipAsync(Vault));

        // Zipped: the whole shield in place of the zipper
        string locked = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));
        Assert.Matches(@"data-password-locked-entry=""vault""[\s\S]*?fa-shield fa-xs[^""]*""[^>]*data-archived-mark=""zipped"" data-encrypted=""true""",
            locked);

        // Unzipped: the halved one, and the lock says only that its password is unlocked
        (_, string? cookie) = await UnlockAsync(Users.Alice, "/docs/vault", Secret);
        await AssertStatus(HttpStatusCode.OK, await ReadWithPasswordAsync("/docs/vault/note.md", Secret));
        string unlocked = await BodyOf(await GetWithCookieAsync(Users.Alice, "/docs", cookie!));
        Assert.Matches(
            @"data-path=""docs/vault""[\s\S]*?data-unlocked-mark[\s\S]*?fa-shield-halved[^""]*""[^>]*data-archived-mark=""unzipped"" data-encrypted=""true""",
            unlocked);
    }

    [Fact]
    public async Task While_it_is_zipped_its_locked_row_shows_the_yellow_bar()
    {
        await EncryptedVaultAsync();
        using ManualResetEventSlim reached = new();
        using ManualResetEventSlim release = new();
        App.Services.GetRequiredService<FolderArchiver>().StepReached = step =>
        {
            if (step != ArchiveStep.ArchiveWritten)
                return;
            reached.Set();
            _ = release.Wait(TimeSpan.FromSeconds(30));
        };
        // (As the lock button does: the browser has it locked again while it is zipped)
        Task<bool> zipping = Task.Run(() => Archives.ZipAsync(Vault, stopsWhenOpened: true), Cancellation);
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), Cancellation));

        string listing = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));
        // (The bar asks how far it got, though the password is locked)
        string polled = await BodyOf(await Client.GetAsync(Users.Alice, "/docs/vault?zipping"));
        release.Set();

        Assert.Matches(@"data-password-locked-entry=""vault""[\s\S]*?data-archived-mark=""zipping"" data-zipping-address=""/docs/vault""" +
                       @"\s+data-encrypted=""true""[\s\S]*?progress-bar bg-warning", listing);
        Assert.Contains("\"zipping\"", polled);
        Assert.True(await zipping);
    }

    [Fact]
    public async Task An_archived_folder_that_is_not_encrypted_keeps_the_zipper()
    {
        Tree.File("docs/plain/note.md", "x");
        JsonObject config = JsonNode.Parse(Cfg.Folder(Cfg.Perms()))!.AsObject();
        config["archive"] = new JsonObject { ["enabled"] = true };
        await AssertStatus(HttpStatusCode.OK, await Client.PostAsync(Users.Admin, "/docs/plain?config", config.ToJsonString()));

        string listing = await BodyOf(await Client.GetAsync(Users.Alice, "/docs"));

        Assert.Matches(@"fa-regular fa-file-zipper[^""]*""[^>]*data-archived-mark=""unzipped"" data-encrypted=""false""", listing);
        Assert.DoesNotContain("fa-shield", listing);
    }

    [Fact]
    public async Task Renaming_it_unzipped_with_its_password_forgotten_asks_for_it_and_then_works()
    {
        await EncryptedVaultAsync();
        (_, string? cookie) = await UnlockAsync(Users.Alice, "/docs/vault", Secret);
        Passwords.Forget(Vault);

        HttpResponseMessage asked = await PostWithCookieAsync(Users.Alice, "/docs/vault?rename=renamed", cookie!);

        await AssertPasswordRequired("/docs/vault", asked);
        Assert.NotNull(Archives.MountedAt(Vault));
        // The page sends it again once the password is typed
        await AssertStatus(HttpStatusCode.OK, await WithPasswordAsync(Users.Alice, HttpMethod.Post, "/docs/vault?rename=renamed", Secret));
        Assert.False(Directory.Exists(Vault));
        Assert.True(File.Exists(Tree.Physical("docs/renamed/_.7z")));
    }

    [Fact]
    public async Task Deleting_what_holds_it_unzipped_with_its_password_forgotten_asks_for_it_and_then_works()
    {
        await EncryptedVaultAsync();
        (_, string? cookie) = await UnlockAsync(Users.Admin, "/docs/vault", Secret);
        Passwords.Forget(Vault);

        AntiforgeryTokens tokens = App.TokensFor(Users.Admin);
        HttpResponseMessage asked = await Client.SendAsync(Users.Admin, HttpMethod.Delete, "/docs", antiforgery: false,
            headers: new Dictionary<string, string> { ["Cookie"] = cookie + "; " + tokens.Cookie, [tokens.HeaderName] = tokens.RequestToken });

        await AssertPasswordRequired("/docs/vault", asked);
        Assert.True(Directory.Exists(Vault));
        Assert.NotNull(Archives.MountedAt(Vault));
        await AssertStatus(HttpStatusCode.OK, await WithPasswordAsync(Users.Admin, HttpMethod.Delete, "/docs", Secret));
        Assert.False(Directory.Exists(Tree.Physical("docs")));
    }

    [Fact]
    public async Task Downloading_it_as_it_is_with_its_password_forgotten_asks_for_it()
    {
        await EncryptedVaultAsync();
        (_, string? cookie) = await UnlockAsync(Users.Admin, "/docs/vault", Secret);
        Passwords.Forget(Vault);

        HttpResponseMessage asked = await PostWithCookieAsync(Users.Admin, "/docs/vault?archive=as-is", cookie!);

        await AssertPasswordRequired("/docs/vault", asked);
        Assert.NotNull(Archives.MountedAt(Vault));
    }
}
