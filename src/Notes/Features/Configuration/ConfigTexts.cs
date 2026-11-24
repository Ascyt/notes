using Notes.Features.Passwords;
using Notes.Features.Permissions;

namespace Notes.Features.Configuration;

/// <summary>
/// The texts of the configuration files, which only those who manage the configuration see: what is wrong with one
/// (<see cref="ConfigParser"/>, <see cref="Migrations.ConfigMigrator"/>, <see cref="Principal"/>), what saving it says
/// (<see cref="ConfigHandler"/>), and what each setting means in the editor (<see cref="ConfigSchema"/>). A site with texts of its own
/// derives from this class and registers it in place of this one, as for <see cref="Components.Toolbar.ToolbarTexts"/>. An error starts
/// with where it is in the file (<c>perms.rwAllow</c>), which is passed in as it is, as are the names of the settings.
/// </summary>
public class ConfigTexts
{
    /// <summary>The texts of the app itself, for what has none of the site's at hand (the parsing of a configuration for its value only).</summary>
    public static ConfigTexts Default { get; } = new();

    // What is wrong with a configuration

    public virtual string NotAVersion(string where) => $"{where}: must be a version like \"1.0.0\" (major.minor.patch).";
    public virtual string NotABoolean(string where) => $"{where}: must be true or false.";
    public virtual string NotAnArray(string where) => $"{where}: must be an array.";
    /// <summary>Where is "" for the whole configuration.</summary>
    public virtual string NotAnObject(string where) => where == "" ? "The configuration must be an object." : $"{where}: must be an object.";
    public virtual string NotAString(string where) => $"{where}: must be a string.";
    public virtual string Empty => "The configuration is empty.";
    public virtual string TooLong => "The configuration is too long.";
    public virtual string NotJson(int line, long column, string why) => $"Not valid JSON (line {line}, column {column}): {why}";
    public virtual string NotJsonAnywhere(string why) => $"Not valid JSON: {why}";

    public virtual string UnknownProperty(string where, IEnumerable<string> allowed)
        => $"{where}: unknown property. Allowed here: {string.Join(", ", allowed)}.";

    public virtual string Repeated(string where) => $"{where}: is there more than once.";
    public virtual string Required(string where) => $"{where}: is required.";
    public virtual string NotAFileName(string where, string name) => $"{where}: '{name}' is not a name a file can have.";
    public virtual string FileTwice(string where, string name) => $"{where}: '{name}' is there more than once.";
    public virtual string NoSuchFile(string where, string name) => $"{where}: there is no file '{name}' in this folder.";

    /// <summary>All users may not be in a list that lets them change something, as those who are not logged in may not.</summary>
    public virtual string EveryoneMayNotChange(string where)
        => $"{where}: '{Principal.Everyone}' (all users, including those who are not logged in) can't be in this list, as they may not change "
            + $"anything. Use '{Principal.LoggedIn}' for all who are logged in. Only rwReadOnly, rwDeny and cdDeny allow '{Principal.Everyone}'.";

    public virtual string NotPrincipals(string where) => $"{where}: must be an array of principals.";
    public virtual string PrincipalTwice(string where, string principal) => $"{where}: '{principal}' is in this list more than once.";

    public virtual string InExclusiveLists(string where, string principal, string list, string otherList)
        => $"{where}: '{principal}' is in both {list} and {otherList}, which exclude each other.";

    // What is wrong with a password

    public virtual string PasswordAndHash(string passwordWhere, string hashWhere)
        => $"{passwordWhere}: can't be given together with {hashWhere}. A new password replaces the hash of the old one when it is saved.";

    public virtual string PasswordNotSaved(string where, string hashWhere)
        => $"{where}: a password can only be set in the editor of the configuration, which keeps nothing but its hash ({hashWhere}). It "
            + "was written into the file in another way.";

    public virtual string NotAPassword(string where, PasswordProblem problem) => problem switch
    {
        PasswordProblem.Empty => $"{where}: is empty. null for no password.",
        PasswordProblem.TooLong => $"{where}: is longer than {PasswordHashing.MaxBytes} bytes (UTF-8), more than is read of a password. A "
            + "character outside of ASCII (an umlaut, an emoji) takes 2 to 4 bytes.",
        _ => $"{where}: must not have control characters in it."
    };

    public virtual string NotAPasswordHash(string where, string passwordWhere)
        => $"{where}: is not the hash of a password as the app makes them. A new password is set with {passwordWhere}.";

    // What is wrong with the archive

    public virtual string NotAFiletype(string where) => $"{where}: must be \"7z\" or \"zip\".";
    public virtual string NotACompressionLevel(string where, int max) => $"{where}: must be a whole number from 0 (none) to {max} (most).";
    public virtual string NotSupportedYet(string where) => $"{where}: not supported yet. Must be false.";

    // What is wrong with a principal (u:alice, g:team, …)

    public virtual string NotAPrincipal(string text)
        => $"'{text}' is not a principal. Write u:<user name>, g:<group>, r:<role>, a for all users or l for all users who are logged in.";

    public virtual string SpacesAroundName(string text) => $"'{text}' must not have spaces around the name, or control characters in it.";

    /// <summary>With the start of the principal only, as it is too long to show.</summary>
    public virtual string PrincipalTooLong(string start) => $"'{start}...' is too long.";

    // The version of the format

    public virtual string VersionRequired(string versionKey, string current)
        => $"version: is required. Write \"{versionKey}\": \"{current}\" (the version of this app's format) as the first property.";

    public virtual string VersionTooNew(string version, string current)
        => $"version: {version} is newer than the {current} that this app reads, so it can't be used. Was it written by a newer version of the app?";

    public virtual string CantBeMigrated => "version: can't be brought up to date, as it is not a valid configuration.";

    public virtual string MigrationFailed(string? toVersion, string why)
        => $"version: could not be migrated to {toVersion ?? "the current version"}: {why}";

    // Saving and showing a configuration

    public virtual string CantBeRead => "The configuration file can't be read by the server.";

    /// <summary>Above what is wrong with it (which follows on the next lines).</summary>
    public virtual string SavedIsInvalid
        => "The saved configuration of this folder is not valid. Until it is fixed, only admins have access to everything under it.";

    public virtual string NotUtf8 => "The configuration isn't valid UTF-8. Nothing was saved.";
    public virtual string CantBeWritten => "The configuration could not be saved: the server can't write it.";

    /// <summary>Above what is wrong (which follows on the next lines).</summary>
    public virtual string FilesNotAdded => "The entries for the files of this folder could not be added to what was sent, so nothing was saved.";

    /// <summary>A password may only be changed or removed by whoever knows it (admins too).</summary>
    public virtual string PasswordChangeNeedsUnlock(string display)
        => $"The password of '{display}' can only be changed or removed while '{display}' is unlocked. Nothing was saved.";

    public virtual string ArchiveOffNotYet
        => "archive.enabled: an archived folder can't be turned back into a normal one yet. Nothing was saved.";

    public virtual string ArchiveFormatNotYet
        => "archive.filetype: the format of an archived folder can't be changed yet (it keeps the one of its archive). Nothing was saved.";

    public virtual string CantArchiveBridge
        => "archive.enabled: the root, a bridge, or a folder with a bridge in it can't be archived. Nothing was saved.";

    public virtual string CantArchiveNow
        => "The folder can't be archived right now: something in it is in use (a file open on the server?). Nothing was saved; try again.";

    public virtual string FolderUnreadable
        => "The configuration of the folder this file is in can't be edited, as it is not valid. The configuration of the folder has to be fixed first.";

    /// <summary>Above what is wrong (which follows on the next lines).</summary>
    public virtual string FolderInvalid
        => "The configuration of the folder this file is in is not valid. The configuration of the folder has to be fixed first.";

    // What each setting means, in the editor

    public virtual string SchemaPerms(bool isFile)
        => isFile ? "The permissions of this file." : "The permissions of this folder. Its files and subfolders that inherit get these permissions.";

    public virtual string SchemaVersion
        => "The version of the format of this file (major.minor.patch). The app keeps it up to date and brings older files up to the current "
            + "version when it reads them: don't change it.";

    public virtual string SchemaGitignore
        => "Whether the history leaves out the files of this folder and of its subfolders (a subfolder with its own configuration decides for "
            + "itself). Configuration files are always kept.";

    public virtual string SchemaArchive
        => "Whether this folder is kept in an archive (_.7z or _.zip in it), which is unzipped while it is used and zipped again after a while.";

    public virtual string SchemaArchiveEnabled => "true keeps the folder in an archive; false keeps it as it is.";
    public virtual string SchemaArchiveFiletype => "The format of the archive. Default 7z.";

    public virtual string SchemaArchiveCompressionLevel
        => "0 (none; for 7z, which always compresses, the least) to 3 (most, slowest). Default 2.";

    public virtual string SchemaArchiveEncrypted => "Encrypts the archive with the folder's password. Not supported yet.";
    public virtual string SchemaArchiveOnLowestLevel => "Archives only the lowest folders below instead of this one. Not supported yet.";

    public virtual string SchemaFiles => "The files of this folder, which the app keeps up to date. A file without an entry has the settings of the folder.";
    public virtual string SchemaFileName => "The name of the file.";

    public virtual string SchemaPassword(bool isFile)
        => (isFile ? "Password that protects this file" : "Password that protects this folder and everything in it")
            + $", admins included; null for none. Saving keeps only its hash (passwordHash). At most {PasswordHashing.MaxBytes} bytes (UTF-8).";

    public virtual string SchemaPasswordHash
        => "Hash of the password, made when saving. Changing or removing it needs the entry unlocked; \"password\": null in its place removes it.";
    public virtual string SchemaPrincipals => "u:<user name>, g:<group>, r:<role>, a for all users or l for all users who are logged in.";

    public virtual string SchemaChangingPrincipals
        => "u:<user name>, g:<group>, r:<role> or l for all users who are logged in. Not a: those who are not logged in may not change anything.";

    public virtual string SchemaInherit
        => "Whether those that none of the lists below mention get what the folder above (for a file: its folder) gives them. If false, they "
            + "get nothing. Only about where this entry's own permissions come from: what is inside a folder still gets the folder's permissions "
            + "if it inherits itself.";

    /// <summary>What a list of principals of the permissions gives them, by its key (<c>rwAllow</c>, …).</summary>
    public virtual string SchemaList(string key) => key switch
    {
        "rwAllow" => "May open (read) and change (write) the contents.",
        "rwReadOnly" => "May open (read) the contents, but not change them.",
        "rwDeny" => "May not open the contents. Beats rwAllow and rwReadOnly.",
        "cdAllow" => "May create entries here, and delete (also move, rename) this one.",
        "cdCreateOnly" => "May create entries here, but not delete.",
        "cdDeny" => "May neither create nor delete. Beats cdAllow and cdCreateOnly.",
        "historyAllow" => "May see earlier states (read-only), also of what was deleted, moved or renamed since. Needs read access (rw) and "
            + "this, now and at that time.",
        "historyExistingOnly" => "May see earlier states (read-only) of what exists now, not of what was deleted, moved or renamed since. Needs "
            + "read access (rw) and this, now and at that time.",
        "historyDeny" => "May not see earlier states, whatever was given at that time. Beats historyAllow and historyExistingOnly.",
        _ => ""
    };

    /// <summary>After each of <see cref="SchemaList"/>.</summary>
    public virtual string SchemaOneListOfEach => "Each principal may be in one of the rw lists, one of the cd lists and one of the history lists.";
}
