namespace Compuse.Requests.Tests;

[TestClass]
public sealed class WindowsPathTests
{
    [TestMethod]
    public void DriveAbsolutePathsAreNormalizedWithoutFileSystemAccess()
    {
        Assert.AreEqual(@"C:\src\a.txt", new PhysicalFileSource(@"C:\src\a.txt").AbsolutePath);
        Assert.AreEqual(@"C:\src\a.txt", new PhysicalFileSource(@"C:/src/a.txt").AbsolutePath);
        Assert.AreEqual(@"C:\bar", new PhysicalFileSource(@"C:\foo\..\bar").AbsolutePath);
        Assert.AreEqual(@"C:\foo\bar", new PhysicalFileSource(@"C:\foo\.\bar").AbsolutePath);
        Assert.AreEqual(@"C:\foo", new PhysicalFileSource(@"C:\foo\").AbsolutePath);
        Assert.AreEqual(@"C:\", new PhysicalFileSource(@"C:\").AbsolutePath);
        Assert.AreEqual(@"C:\", new PhysicalFileSource(@"C:\foo\bar\..\..").AbsolutePath);
        Assert.AreEqual(@"d:\Temp\file.txt", new PhysicalFileSource(@"d:\Temp\file.txt").AbsolutePath);
        Assert.AreEqual(@"C:\文档\file.txt", new PhysicalFileSource(@"C:\文档\file.txt").AbsolutePath);
    }

    [TestMethod]
    public void UncPathsAreNormalized()
    {
        Assert.AreEqual(@"\\server\share", new FilesystemContainerTarget(@"\\server\share").AbsolutePath);
        Assert.AreEqual(@"\\server\share", new FilesystemContainerTarget(@"\\server\share\").AbsolutePath);
        Assert.AreEqual(@"\\server\share\dir", new FilesystemContainerTarget(@"//server/share/dir").AbsolutePath);
        Assert.AreEqual(@"\\server\share\bar", new FilesystemContainerTarget(@"\\server\share\foo\..\bar").AbsolutePath);
    }

    [TestMethod]
    public void RelativeDeviceAndMalformedPathsAreRejected()
    {
        AssertRejected(@"src\a.txt");
        AssertRejected(@"\src\a.txt");
        AssertRejected(@"C:src\a.txt");
        AssertRejected(@"C:");
        AssertRejected(@"\\?\C:\src\a.txt");
        AssertRejected(@"\\.\C:\src\a.txt");
        AssertRejected(@"\??\C:\src\a.txt");
        AssertRejected(@"\\server");
        AssertRejected(@"\\server\");
        AssertRejected(@"\\\server\share");
        AssertRejected(string.Empty);
        AssertRejected(" C:\\src\\a.txt");
        AssertRejected("C:\\src\\a.txt ");
        AssertRejected("C:\\src\\a.txt\t");
        AssertRejected("C:\\foo\0bar");
        AssertRejected(@"C:\foo*");
        AssertRejected(@"C:\foo?");
        AssertRejected(@"C:\foo:stream");
        AssertRejected(@"C:\foo""bar");
        AssertRejected(@"C:\..");
        AssertRejected(@"C:\foo\\bar");
        AssertRejected(@"C:\foo ");
        AssertRejected(@"C:\foo.");
        AssertRejected(@"C:\CON");
        AssertRejected(@"C:\con.txt");
        AssertRejected(@"C:\PRN\file.txt");
        AssertRejected(@"\\server\share\..");
        AssertRejected(new string('a', DropFilesRequestLimits.MaxPathLength + 1));
        AssertRejected(@"C:\" + new string('a', DropFilesRequestLimits.MaxComponentLength + 1));
    }

    [TestMethod]
    public void LongButLegalPathsAreAccepted()
    {
        string path = @"C:\" + new string('a', 240) + @"\" + new string('b', 240) + ".txt";
        Assert.AreEqual(path, new PhysicalFileSource(path).AbsolutePath);
    }

    private static void AssertRejected(string path)
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() => new PhysicalFileSource(path));
        _ = Assert.ThrowsExactly<ArgumentException>(() => new FilesystemContainerTarget(path));
    }
}
