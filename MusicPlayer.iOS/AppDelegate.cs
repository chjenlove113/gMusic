﻿using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.CloudDrive;
using BigTed;
using Foundation;
using Localizations;
using UIKit;
using MusicPlayer.Api;
using MusicPlayer.Api.GoogleMusic;
using MusicPlayer.iOS.ViewControllers;
using MusicPlayer.Managers;
using MusicPlayer.iOS.Playback;
using MusicPlayer.Data;
using System.Threading.Tasks;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;

namespace MusicPlayer.iOS
{
	// The UIApplicationDelegate for the application. This class is responsible for launching the 
	// User Interface of the application, as well as listening (and optionally responding) to 
	// application events from iOS.
	[Register("AppDelegate")]
	public partial class AppDelegate : UIApplicationDelegate
	{
		public const int AppId = 708727021;
		public const string AppName = "gMusic";
		// class-level declarations
		public static UIWindow window;
		//
		// This method is invoked when the application has loaded and is ready to run. In this 
		// method you should instantiate the window, load the UI into it and then make the window
		// visible.
		//
		// You have 17 seconds to return from this method, or iOS will terminate your application.
		//

		nint playingBackground;
		public UIApplicationShortcutItem LaunchedShortcutItem { get; set; }
		public override bool FinishedLaunching(UIApplication app, NSDictionary launchOptions)
		{
			bool handled = true;
			SimpleAuth.Providers.Twitter.Init();
			AppCenter.Start(ApiConstants.MobileCenterApiKey,
			                	#if ADHOC
			                   typeof(Microsoft.AppCenter.Distribute.Distribute),
								#endif
							   typeof(Analytics),
							   typeof(Crashes));

			// Get possible shortcut item
			if (launchOptions != null && UIApplication.LaunchOptionsShortcutItemKey != null)
			{
				LaunchedShortcutItem = launchOptions[UIApplication.LaunchOptionsShortcutItemKey] as UIApplicationShortcutItem;
				handled = (LaunchedShortcutItem == null);
			}
#if TESTCLOUD
			Xamarin.Calabash.Start();
#endif
			var screenBounds = UIScreen.MainScreen.Bounds;

			Images.MaxScreenSize = (float)NMath.Max(screenBounds.Width, screenBounds.Height);
			SetUpApp(app);
			app.BeginReceivingRemoteControlEvents();
			// create a new window instance based on the screen size
			window = new UIWindow(screenBounds);
			window.TintColor = Style.DefaultStyle.AccentColor;
			// If you have defined a view, add it here:
			// window.RootViewController  = navigationController;

			// make the window visible
			window.MakeKeyAndVisible();
			window.RootViewController = new RootViewController();

			CheckLogin();
			//TODO: uncomment this to work on the Secondary Screen/Car interface.
			//TestCarInterface ();
			return handled;
		}

		public override void PerformActionForShortcutItem(UIApplication application, UIApplicationShortcutItem shortcutItem, UIOperationHandler completionHandler)
		{
			completionHandler(HandleShortcut(shortcutItem));
		}
#if DEBUG
		UIWindow carTestWindow;
		public void TestCarInterface()
		{

			carTestWindow = new UIWindow(UIScreen.MainScreen.Bounds);
			carTestWindow.Tag = 1;
			var style = carTestWindow.GetStyle();
			carTestWindow.TintColor = style.AccentColor;
			if (carTestWindow.RootViewController == null)
				carTestWindow.RootViewController = new Car.CarHeadViewController();

			carTestWindow.Hidden = false;
		}
#endif
		void CheckLogin()
		{
			if (ApiManager.Shared.Count == 0 && !Settings.IPodOnly)
			{
				window.RootViewController.PresentViewController(new IntroViewController(), false, null);
			}
		}

		public void SetUpApp(UIApplication app)
		{
			try
			{
				Strings.Culture = new System.Globalization.CultureInfo(NSLocale.CurrentLocale.LanguageCode);
			}
			catch (Exception ex)
			{
				LogManager.Shared.Log($"Error setting Culture {System.Threading.Thread.CurrentThread.CurrentCulture}");
			}
			SimpleAuth.OnePassword.Activate();
			ApiManager.Shared.Load();
			App.AlertFunction = (t, m) => { new UIAlertView(t, m, null, "Ok").Show(); };
			App.Invoker = app.BeginInvokeOnMainThread;
			App.OnPlaying = () =>
			{
				if (playingBackground != 0)
					return;
				playingBackground = app.BeginBackgroundTask(() =>
				{
					app.EndBackgroundTask(playingBackground);
					playingBackground = 0;
				});
			};
			App.OnStopped = () =>
			{
				if (playingBackground == 0)
					return;
				app.EndBackgroundTask(playingBackground);
				playingBackground = 0;
			};

			App.OnShowSpinner = (title) => { BTProgressHUD.ShowContinuousProgress(title, ProgressHUD.MaskType.Clear); };

			App.OnDismissSpinner = BTProgressHUD.Dismiss;
			App.OnCheckForOffline = (message) =>
			{

				var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
				new AlertView(Strings.DoYouWantToContinue, message){
					{Strings.Continue,() => tcs.TrySetResult (true)},
					{Strings.Nevermind,() => tcs.TrySetResult (false),true},
				}.Show(window.RootViewController);
				return tcs.Task;
			};
			NotificationManager.Shared.LanguageChanged += (s, e) =>
			{
				window.RootViewController = new RootViewController();
			};
#pragma warning disable 4014
			App.Start();
#pragma warning restore 4014
			AutolockPowerWatcher.Shared.CheckStatus();
		}

		public override void HandleEventsForBackgroundUrl(UIApplication application, string sessionIdentifier, Action completionHandler)
		{
			BackgroundDownloadManager.Shared.Init();
			BackgroundDownloadManager.Shared.RepairFromBackground(sessionIdentifier, completionHandler);
		}
		public override void OnResignActivation(UIApplication application)
		{
			PlaybackManager.Shared.NativePlayer.DisableVideo();
		}
		public override void OnActivated(UIApplication application)
		{
			foreach (var w in application.Windows)
			{
				if (w != null && w != window)
				{
					foreach (var v in w.Subviews)
					{
						var desc = v.Description;
						if (desc.Contains("UIReplicantView"))
						{
							v.RemoveFromSuperview();
							LogManager.Shared.Report(new Exception("Had to remove apples view to fix their stupid bug..."));
						}
					}
				}
			}
			PlaybackManager.Shared.NativePlayer.EnableVideo();
			HandleShortcut(LaunchedShortcutItem);
			LaunchedShortcutItem = null;
			ScreenManager.Shared.OnActivated();
		}

		public override void WillEnterForeground(UIApplication application)
		{
			window.RootViewController.ViewDidAppear(true);
			ScreenManager.Shared.WillEnterForeground();
		}

		public override bool ContinueUserActivity(UIApplication application, NSUserActivity userActivity, UIApplicationRestorationHandler completionHandler)
		{
			NSObject idObj;
			if (!userActivity.UserInfo.TryGetValue(new NSString("kCSSearchableItemActivityIdentifier"), out idObj))
			{
				return false;
			}
			var id = idObj.ToString();
			PlaybackManager.Shared.PlaySong(id);
			return true;
		}


		bool HandleShortcut(UIApplicationShortcutItem shortcutItem)
		{
			if (shortcutItem == null)
				return false;

			switch (shortcutItem.Type)
			{
				//play
				case "com.IIS.MusicPlayer.iOS.000":
					PlaybackManager.Shared.Play();
					return true;
				case "com.IIS.MusicPlayer.iOS.001":

					var vm = new MusicPlayer.ViewModels.RadioStationViewModel { IsIncluded = false };
					var items = vm.RowsInSection(0);
					if (items == 0)
						return true;
					var radio = vm.ItemFor(0, 0);
					Settings.ShowOfflineOnly = false;
					PlaybackManager.Shared.Play(radio);
					return true;
			}

			return false;
		}

		public override void DidEnterBackground(UIApplication application)
		{
			ScreenManager.Shared.DidEnterBackground();
		}

		public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
		{
			if (SimpleAuth.Native.OpenUrl(app, url, options))
				return true;
			return false;
		}
	}
}