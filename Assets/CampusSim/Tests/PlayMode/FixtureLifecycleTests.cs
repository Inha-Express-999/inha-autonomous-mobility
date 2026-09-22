using System;
using System.Collections;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;
using InhaExpress.Client.Presentation;
using InhaExpress.Client.PC;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed class FixtureLifecycleTests
    {
        [UnityTest]
        public IEnumerator PresenterDisableEnableRebindAndHostDestructionCleanUpSubscriptions()
        {
            var hostObject = new GameObject("Test Host");
            var viewObject = new GameObject("Test View");
            var source = new FixtureClientDataSource(ClientRole.PC_Operator, "0.1.4.0", sessionId: "lifecycle");
            try
            {
                var host = hostObject.AddComponent<ClientRuntimeHost>();
                // Drive the source clock explicitly, independent of the Editor's real-time clock.
                host.enabled = false;
                host.Initialize(ClientRole.PC_Operator, source);
                var view = viewObject.AddComponent<OperatorStatusPresenter>();
                view.Bind(host);
                view.Bind(host);
                source.Start(0);
                source.Pump(4);
                StringAssert.Contains("ASSIGNED", view.Body);
                view.enabled = false;
                var frozen = view.Body;
                source.Pump(14);
                Assert.That(view.Body, Is.EqualTo(frozen));
                view.enabled = true;
                StringAssert.Contains("IN_TRANSIT", view.Body);
                UnityEngine.Object.Destroy(hostObject);
                yield return null;
                Assert.Throws<ObjectDisposedException>(() => source.Pump(15));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewObject);
                if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
                source.Dispose();
            }
        }
    }
}
