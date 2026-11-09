using System;
using InhaExpress.Client.MapClient;
using NUnit.Framework;

namespace InhaExpress.Client.Tests
{
    public sealed class ClientServerSettingsTests
    {
        [TestCase("192.168.0.10", "http://192.168.0.10:8765")]
        [TestCase(" 192.168.0.10:8765/ ", "http://192.168.0.10:8765")]
        [TestCase("http://192.168.0.10:9000", "http://192.168.0.10:9000")]
        [TestCase("https://campus.example", "https://campus.example")]
        public void NormalizesServerAddress(string input, string expected) =>
            Assert.That(ClientServerSettings.NormalizeUrl(input), Is.EqualTo(expected));

        [TestCase("")]
        [TestCase("0.0.0.0:8765")]
        [TestCase("http://pc:8765/demo/")]
        [TestCase("file:///C:/server")]
        [TestCase("http://user:password@pc:8765")]
        [TestCase("http://pc:8765?server=other")]
        public void RejectsNonServerAddresses(string input) =>
            Assert.Throws<ArgumentException>(() => ClientServerSettings.NormalizeUrl(input));
    }
}
