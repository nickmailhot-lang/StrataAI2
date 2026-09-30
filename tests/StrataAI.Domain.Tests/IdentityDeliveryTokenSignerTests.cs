using StrataAI.Application.Identity;
using StrataAI.Infrastructure.Identity;

namespace StrataAI.Domain.Tests;

public sealed class IdentityDeliveryTokenSignerTests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    private static readonly string SecondKey = Convert.ToBase64String(Enumerable.Range(33, 32).Select(i => (byte)i).ToArray());

    [Fact]
    public void ARCH_07_TC_01_TokenCanBeRecoveredAfterWorkerRestartWithoutPersistingIt()
    {
        var id = Guid.NewGuid();
        using var api = new IdentityDeliveryTokenSigner("key1", new Dictionary<string, string> { ["key1"] = Key });
        var token = api.Derive(id, IdentityTokenPurpose.ResetPassword, api.CurrentKeyId);
        using var restartedWorker = new IdentityDeliveryTokenSigner("key1", new Dictionary<string, string> { ["key1"] = Key });
        Assert.Equal(token, restartedWorker.Derive(id, IdentityTokenPurpose.ResetPassword, "key1"));
        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
        Assert.DoesNotContain(id.ToString("N"), token);
    }

    [Fact]
    public void PRD_24_TC_04_PurposeIdentityAndKeyVersionCannotBeInterchanged()
    {
        using var signer = new IdentityDeliveryTokenSigner("key1", new Dictionary<string, string> { ["key1"] = Key, ["alias"] = Key });
        var id = Guid.NewGuid();
        var token = signer.Derive(id, IdentityTokenPurpose.ResetPassword, "key1");
        Assert.NotEqual(token, signer.Derive(id, IdentityTokenPurpose.VerifyEmail, "key1"));
        Assert.NotEqual(token, signer.Derive(Guid.NewGuid(), IdentityTokenPurpose.ResetPassword, "key1"));
        Assert.NotEqual(token, signer.Derive(id, IdentityTokenPurpose.ResetPassword, "alias"));
    }

    [Fact]
    public void ARCH_07_TC_01_RotationRetainsOldQueuedTokenWhileNewIssuesUseNewKey()
    {
        var id = Guid.NewGuid();
        using var old = new IdentityDeliveryTokenSigner("old", new Dictionary<string, string> { ["old"] = Key });
        using var rotated = new IdentityDeliveryTokenSigner("new", new Dictionary<string, string> { ["old"] = Key, ["new"] = SecondKey });
        Assert.Equal("new", rotated.CurrentKeyId);
        Assert.Equal(old.Derive(id, IdentityTokenPurpose.VerifyEmail, "old"), rotated.Derive(id, IdentityTokenPurpose.VerifyEmail, "old"));
        Assert.NotEqual(rotated.Derive(id, IdentityTokenPurpose.VerifyEmail, "old"), rotated.Derive(id, IdentityTokenPurpose.VerifyEmail, "new"));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void PRD_24_TC_03_InvalidKeySizeFailsClosed(int size) =>
        Assert.Throws<ArgumentException>(() => new IdentityDeliveryTokenSigner("key", new Dictionary<string, string> { ["key"] = Convert.ToBase64String(new byte[size]) }));

    [Fact]
    public void PRD_24_TC_03_InvalidKeyRingAndMissingVersionsFailWithoutSecretDisclosure()
    {
        Assert.Throws<ArgumentException>(() => new IdentityDeliveryTokenSigner("missing", new Dictionary<string, string> { ["key"] = Key }));
        Assert.Throws<ArgumentException>(() => new IdentityDeliveryTokenSigner("key", new Dictionary<string, string> { ["key"] = "sensitive-invalid-secret" }));
        Assert.Throws<ArgumentException>(() => new IdentityDeliveryTokenSigner("bad:key", new Dictionary<string, string> { ["bad:key"] = Key }));
        using var signer = new IdentityDeliveryTokenSigner("key", new Dictionary<string, string> { ["key"] = Key });
        var error = Assert.Throws<InvalidOperationException>(() => signer.Derive(Guid.NewGuid(), IdentityTokenPurpose.VerifyEmail, "missing"));
        Assert.DoesNotContain(Key, error.Message);
        Assert.Throws<ArgumentException>(() => signer.Derive(Guid.Empty, IdentityTokenPurpose.VerifyEmail, "key"));
        Assert.Throws<ArgumentOutOfRangeException>(() => signer.Derive(Guid.NewGuid(), (IdentityTokenPurpose)99, "key"));
        signer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => signer.Derive(Guid.NewGuid(), IdentityTokenPurpose.VerifyEmail, "key"));
    }
}
