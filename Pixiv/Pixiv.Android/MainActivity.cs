﻿using System;

using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using System.IO;
using System.Threading.Tasks;
using AndroidX.Core.App;

namespace Pixiv.Droid
{
    [Activity(Label = "Pixiv", Icon = "@mipmap/icon", Theme = "@style/MainTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize )]
    public class MainActivity : global::Xamarin.Forms.Platform.Android.FormsAppCompatActivity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            TabLayoutResource = Resource.Layout.Tabbar;
            ToolbarResource = Resource.Layout.Toolbar;

            base.OnCreate(savedInstanceState);

            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            global::Xamarin.Forms.Forms.Init(this, savedInstanceState);

            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            AndroidEnvironment.UnhandledExceptionRaiser += AndroidEnvironment_UnhandledExceptionRaiser;

            CreateNotificationChannel();

            CreateNotification();
            LoadApplication(new App(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath));
        }

        static void CurrentDomain_FirstChanceException(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            Log("FirstChanceException", e.Exception);
        }

        static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Log("TaskScheduler", e.Exception);
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log("Domain", e.ExceptionObject);
        }

        static void AndroidEnvironment_UnhandledExceptionRaiser(object sender, RaiseThrowableEventArgs e)
        {
            Log("Android", e.Exception);
        }

        static void Log(string name, object e)
        {
            string s = System.Environment.NewLine;

            File.AppendAllText($"/storage/emulated/0/pixiv.{name}.txt", $"{s}{s}{s}{s}{DateTime.Now}{s}{e}", System.Text.Encoding.UTF8);
        }

        const string CHANNEL_ID = "53453435465435";
       
        void CreateNotification()
        {
            // Instantiate the builder and set notification elements:
            NotificationCompat.Builder builder = new NotificationCompat.Builder(this, CHANNEL_ID)
                .SetContentTitle("Sample Notification")
                .SetContentText("Hello World! This is my first notification!")
                .SetSmallIcon(Resource.Drawable.notify_panel_notification_icon_bg)
                .SetOngoing(true);

            // Build the notification:
            Notification notification = builder.Build();

            // Get the notification manager:
            NotificationManager notificationManager =
                GetSystemService(NotificationService) as NotificationManager;

            // Publish the notification:
            const int notificationId = 985852343;
            notificationManager.Notify(notificationId, notification);
        }

        void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            {
                // Notification channels are new in API 26 (and not a part of the
                // support library). There is no need to create a notification
                // channel on older versions of Android.
                return;
            }

            var channel = new NotificationChannel(CHANNEL_ID, "PIXIV", NotificationImportance.Default);

            var notificationManager = (NotificationManager)GetSystemService(NotificationService);
            notificationManager.CreateNotificationChannel(channel);
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }
}