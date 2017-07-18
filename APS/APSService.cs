using System;
using System.Configuration;
using System.Diagnostics;
using System.ServiceProcess;
using APS.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PushSharp.Apple;

namespace APS
{
    partial class APSService : ServiceBase
    {
        #region Private Constants

        private const string EVENT_SOURCE = "APS Service";
        private const string EVENT_LOG = "APS Service";

        #endregion

        #region Private Variables

        private ApnsServiceBroker apnsBroker;
        private FeedbackService fbs;
        private EventLog eventLog;
        private bool processing = false;

        #endregion

        public APSService()
        {
            InitializeComponent();

            #region EventLog

            // I'm using EventLog to log errors and messages
            eventLog = new EventLog();

            // Create event source if necessary
            if (!EventLog.SourceExists(EVENT_SOURCE))
                EventLog.CreateEventSource(EVENT_SOURCE, EVENT_LOG);

            eventLog.Source = EVENT_SOURCE;
            eventLog.Log = EVENT_LOG;

            #endregion
        }

        protected override void OnStart(string[] args)
        {
            #region Init APS

            var certificateFilePath = ConfigurationManager.AppSettings["CertificateLocation"];
            var certificatePassword = ConfigurationManager.AppSettings["CertificatePassword"];

            var config = new ApnsConfiguration(ApnsConfiguration.ApnsServerEnvironment.Production, certificateFilePath, certificatePassword);

            // Create a new broker
            apnsBroker = new ApnsServiceBroker(config);

            // Wire up events
            apnsBroker.OnNotificationFailed += (notification, aggregateEx) =>
            {
                aggregateEx.Handle(ex =>
                {
                    // See what kind of exception it was to further diagnose
                    if (ex is ApnsNotificationException)
                    {
                        var notificationException = (ApnsNotificationException)ex;
                        // Deal with the failed notification
                        var apnsNotification = notificationException.Notification;
                        var statusCode = notificationException.ErrorStatusCode;
                        var deviceToken = notificationException.Notification.DeviceToken;

                        eventLog.WriteEntry(string.Format("Apple Notification Failed: ID={0}, Code={1}, Device Token={2}", apnsNotification.Identifier, statusCode, deviceToken));
                    }
                    else
                    {
                        // Inner exception might hold more useful information like an ApnsConnectionException           
                        eventLog.WriteEntry(string.Format("Apple Notification Failed for some unknown reason : {0}", ex.InnerException));
                    }

                    // Mark it as handled
                    return true;
                });
            };

            apnsBroker.OnNotificationSucceeded += (notification) => { };

            apnsBroker.Start();

            #endregion

            #region Feedback Service

            fbs = new FeedbackService(config);

            fbs.FeedbackReceived += (string deviceToken, DateTime timestamp) =>
            {
                try
                {
                    // Removes token from the database
                    PushQueue.RemoveToken(deviceToken);
                    eventLog.WriteEntry(String.Format("Removed token: {0}", deviceToken));
                }
                catch (Exception ex)
                {
                    eventLog.WriteEntry(String.Format("Error deleting token {0}\r\nError: {1}", deviceToken, ex.Message));
                }
            };

            #endregion

            #region Timer

            var timer = new System.Timers.Timer()
            {
                Interval = 60000 // 60 seconds
            };
            timer.Elapsed += new System.Timers.ElapsedEventHandler(OnTimer);
            timer.Start();

            #endregion
        }

        protected override void OnStop()
        {
            apnsBroker.Stop();
        }

        public void OnTimer(object sender, System.Timers.ElapsedEventArgs args)
        {
            if (!processing)
            {
                try
                {
                    processing = true;

                    // Pull everything from the queue via DAL
                    var pushQueue = PushQueue.Read();

                    if (pushQueue.Count > 0)
                    {
                        foreach (var push in pushQueue)
                        {
                            // The message is stored in the database and needs to be JSON escaped
                            var message = JsonConvert.SerializeObject(push.alert_message);

                            var apnsPayload = String.Format("{{ 'aps': {{ 'alert' : {0}, 'badge' : {1} }}, 'magazine_id' : {2}, 'issue_id' : '{3}' }}"
                                                        , message
                                                        , push.badge
                                                        , push.magazine_id
                                                        , push.issue_id);

                            apnsBroker.QueueNotification(new ApnsNotification
                            {
                                DeviceToken = push.device_token,
                                Payload = JObject.Parse(apnsPayload)
                            });

                            try
                            {
                                // Remove the message from the queue
                                push.Delete();
                            }
                            catch (Exception ex)
                            {
                                eventLog.WriteEntry(String.Format("Error deleting push notification (ID = {0})\r\nError: {1}", push.id, ex.Message));
                            }
                        }
                    }


                }
                catch (Exception ex)
                {
                    eventLog.WriteEntry(String.Format("Error processing.\r\nError: {0}", ex.Message));
                }
                finally
                {
                    processing = false;
                }
            }

            // Check Feedback service every interval
            fbs.Check();
        }
    }
}
