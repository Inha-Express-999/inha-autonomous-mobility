using System;
using System.Diagnostics;
using System.Threading;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Networking;

internal static class UnityClientSmoke
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--dto-self-test")
            return RunSensorDtoSelfTest();
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: UnityClientSmoke.exe <websocket-url>");
            return 64;
        }

        var mobileSnapshots = 0;
        var requestCount = 0;
        var routeSpeedProfileValid = false;
        var routeCount = 0;
        var routePolylinePointCount = 0;
        var routeSegmentSpeedCount = 0;
        var acknowledgements = 0;
        var accepted = false;
        var sawReconnect = false;
        var replayedCommand = false;
        var localizationAcknowledged = false;
        var localizationAccepted = false;
        var localizationSessionId = string.Empty;
        var sensorObservationAcknowledged = false;
        var sensorObservationAccepted = false;
        var operatorPassengerAccepted = false;
        var operatorCargoAccepted = false;
        var operatorCancelAccepted = false;
        var operatorCancelSent = false;
        var operatorCargoRequestId = string.Empty;

        using (var operatorSource = new WebSocketClientDataSource(
                   args[0], ClientRole.PC_Operator, "0.2.3.0"))
        using (var passengerSource = new WebSocketClientDataSource(
                   args[0], ClientRole.Mobile_Passenger, "0.2.3.0", "ws-smoke-passenger"))
        {
            Action<WorldSnapshotDto, double> inspectRouteProfiles = (snapshot, _) =>
            {
                foreach (var route in snapshot.Routes)
                {
                    routeCount = snapshot.Routes.Count;
                    routePolylinePointCount = route.Polyline.Count;
                    routeSegmentSpeedCount = route.SegmentSpeedsMps.Count;
                    if (route.Polyline.Count < 2 ||
                        route.SegmentSpeedsMps.Count != route.Polyline.Count - 1)
                        continue;
                    routeSpeedProfileValid = true;
                    foreach (var speed in route.SegmentSpeedsMps)
                        if (speed <= 0.0) routeSpeedProfileValid = false;
                }
            };
            operatorSource.SnapshotReceived += inspectRouteProfiles;
            passengerSource.SnapshotReceived += (snapshot, receivedAtS) =>
            {
                mobileSnapshots++;
                requestCount = snapshot.Requests.Count;
                inspectRouteProfiles(snapshot, receivedAtS);
            };
            passengerSource.CommandAcknowledged += ack =>
            {
                acknowledgements++;
                accepted = ack.Accepted;
            };
            operatorSource.EgoLocalizationAcknowledged += ack =>
            {
                localizationAcknowledged = true;
                localizationAccepted = ack.Accepted && ack.ObservedTick == 1;
                localizationSessionId = ack.SessionId;
            };
            operatorSource.SensorObservationAcknowledged += ack =>
            {
                sensorObservationAcknowledged = true;
                sensorObservationAccepted = ack.Accepted && ack.SensorId == "front-lidar" && ack.ObservedTick == 1;
            };
            operatorSource.CommandAcknowledged += ack =>
            {
                if (ack.MessageId == "stable-operator-passenger")
                    operatorPassengerAccepted = ack.Accepted && ack.Request?.OwnerId == "pc-operator";
                if (ack.MessageId == "stable-operator-cargo")
                {
                    operatorCargoAccepted = ack.Accepted && ack.Request?.ServiceType == ServiceType.CARGO;
                    operatorCargoRequestId = ack.Request?.Id ?? string.Empty;
                }
                if (ack.MessageId == "stable-operator-cancel")
                    operatorCancelAccepted = ack.Accepted && ack.Request?.Status == RequestStatus.CANCELLED;
            };

            var timer = Stopwatch.StartNew();
            operatorSource.Start(0);
            passengerSource.Start(0);
            while (timer.Elapsed.TotalSeconds < 8 &&
                   (operatorSource.ConnectionState != ConnectionState.Connected ||
                    passengerSource.ConnectionState != ConnectionState.Connected || mobileSnapshots == 0))
            {
                var now = timer.Elapsed.TotalSeconds;
                operatorSource.Pump(now);
                passengerSource.Pump(now);
                Thread.Sleep(10);
            }

            if (operatorSource.ConnectionState != ConnectionState.Connected ||
                passengerSource.ConnectionState != ConnectionState.Connected || mobileSnapshots == 0)
            {
                Console.Error.WriteLine("Both roles did not connect and receive their initial snapshots.");
                return 2;
            }

            operatorSource.SendEgoLocalization(new EgoLocalizationDto(
                "V01", 1, "synthetic-campus-6stop-v1",
                new MapPositionDto(0.0, 0.0, 0.0), 0.0, 0.0));
            operatorSource.SendSensorObservation(new SensorObservationDto(
                "V01", "front-lidar", SensorType.LIDAR_2D, 1, 1,
                "synthetic-campus-6stop-v1", true,
                new[]
                {
                    new SensorDetectionDto(5.0, 0.2,
                        new MapPositionDto(4.900332889, 0.993346654, 0.0),
                        SensorEntityClass.PEDESTRIAN)
                }));
            operatorSource.SendPassengerRequest(new PassengerRequestCommandDto(
                "fixture_landmark_2", "fixture_landmark_1",
                new ServiceNeedsDto(false, 0, false), messageId: "stable-operator-passenger"));
            operatorSource.SendCargoRequest(new CargoRequestCommandDto(
                "fixture_landmark_2", "fixture_landmark_1", 5.0,
                messageId: "stable-operator-cargo"));

            // The test server intentionally discards the first passenger ACK and closes
            // that connection. The client must replay the same command after reconnect.
            passengerSource.SendPassengerRequest(new PassengerRequestCommandDto(
                "fixture_landmark_2", "fixture_landmark_1",
                new ServiceNeedsDto(false, 0, false), messageId: "stable-smoke-command"));

            while (timer.Elapsed.TotalSeconds < 20)
            {
                var now = timer.Elapsed.TotalSeconds;
                operatorSource.Pump(now);
                passengerSource.Pump(now);
                if (operatorCargoAccepted && !operatorCancelSent &&
                    !string.IsNullOrWhiteSpace(operatorCargoRequestId))
                {
                    operatorCancelSent = true;
                    operatorSource.SendCancelRequest(new CancelRequestCommandDto(
                        operatorCargoRequestId, "stable-operator-cancel"));
                }
                if (passengerSource.ConnectionState == ConnectionState.Reconnecting)
                    sawReconnect = true;

                if (sawReconnect && passengerSource.ConnectionState == ConnectionState.Connected &&
                    acknowledgements == 1 && requestCount == 1 && localizationAcknowledged &&
                    sensorObservationAcknowledged &&
                    operatorPassengerAccepted && operatorCargoAccepted && operatorCancelAccepted &&
                    routeSpeedProfileValid)
                {
                    replayedCommand = true;
                    break;
                }

                Thread.Sleep(10);
            }

            Console.WriteLine(
                "operator={0}; localizationAck={1}; localizationAccepted={2}; sensorAck={3}; sensorAccepted={4}; sessionId={5}; " +
                "passenger={6}; snapshots={7}; requestCount={8}; ackCount={9}; accepted={10}; " +
                "operatorPassenger={11}; operatorCargo={12}; operatorCancel={13}; " +
                "routeSpeedProfileValid={14}; routes={15}; points={16}; speeds={17}; " +
                "sawReconnect={18}; replayed={19}; elapsed={20:F2}s",
                operatorSource.ConnectionState, localizationAcknowledged, localizationAccepted,
                sensorObservationAcknowledged, sensorObservationAccepted,
                localizationSessionId, passengerSource.ConnectionState, mobileSnapshots,
                requestCount, acknowledgements, accepted, operatorPassengerAccepted,
                operatorCargoAccepted, operatorCancelAccepted, routeSpeedProfileValid,
                routeCount, routePolylinePointCount, routeSegmentSpeedCount, sawReconnect, replayedCommand,
                timer.Elapsed.TotalSeconds);

            return replayedCommand && accepted && acknowledgements == 1 && requestCount == 1 &&
                   localizationAcknowledged && localizationAccepted &&
                   sensorObservationAcknowledged && sensorObservationAccepted &&
                   operatorPassengerAccepted && operatorCargoAccepted && operatorCancelAccepted &&
                   routeSpeedProfileValid &&
                   !string.IsNullOrWhiteSpace(localizationSessionId) ? 0 : 3;
        }
    }

    private static int RunSensorDtoSelfTest()
    {
        var routePoints = new[]
        {
            new MapPositionDto(0.0, 0.0, 0.0),
            new MapPositionDto(1.0, 0.0, 0.0)
        };
        var route = new RouteDto("route", "synthetic-v1", routePoints,
            segmentSpeedsMps: new[] { 0.2 });
        if (route.SegmentSpeedsMps.Count != 1 || route.SegmentSpeedsMps[0] != 0.2)
            throw new InvalidOperationException("Route segment speed profile was not retained.");
        ExpectThrows<ArgumentException>(() => new RouteDto("route", "synthetic-v1", routePoints,
            segmentSpeedsMps: new[] { 0.2, 0.3 }));
        ExpectThrows<ArgumentOutOfRangeException>(() => new RouteDto("route", "synthetic-v1", routePoints,
            segmentSpeedsMps: new[] { 0.0 }));

        var local = new MapPositionDto(5.0 * Math.Cos(0.2), 5.0 * Math.Sin(0.2), 0.1);
        _ = new SensorDetectionDto(5.0, 0.2, local, SensorEntityClass.PEDESTRIAN);
        ExpectThrows<ArgumentException>(() => new SensorDetectionDto(
            4.0, 0.2, local, SensorEntityClass.PEDESTRIAN));
        ExpectThrows<ArgumentException>(() => new SensorDetectionDto(
            5.0, 0.4, local, SensorEntityClass.PEDESTRIAN));

        var moving = new SensorDetectionDto(5.0, 0.2,
            new MapPositionDto(5.0 * Math.Cos(0.2), 5.0 * Math.Sin(0.2), 0.0),
            SensorEntityClass.VEHICLE, 1.0);
        ExpectThrows<ArgumentException>(() => new SensorObservationDto(
            "V01", "front-lidar", SensorType.LIDAR_2D, 1, 1, "map-v1", true,
            new[] { moving }));

        var tooMany = new SensorDetectionDto[65];
        for (int index = 0; index < tooMany.Length; index++)
            tooMany[index] = new SensorDetectionDto(5.0, 0.2,
                new MapPositionDto(5.0 * Math.Cos(0.2), 5.0 * Math.Sin(0.2), 0.0),
                SensorEntityClass.UNKNOWN);
        ExpectThrows<ArgumentOutOfRangeException>(() => new SensorObservationDto(
            "V01", "front-lidar", SensorType.LIDAR_2D, 1, 1, "map-v1", true, tooMany));
        Console.WriteLine("Sensor and route DTO edge-case self-test passed.");
        return 0;
    }

    private static void ExpectThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("Expected exception " + typeof(TException).Name + ".");
    }
}
