using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using OpenIDE.Core.Config;
using OpenIDE.Core.Language;
using OpenIDE.Core.Profiles;

namespace OpenIDE.Arguments.Handlers
{
	class ConfigurationHandler : ICommandHandler
	{
		private PluginLocator _pluginLocator;
		private Action<string> _eventDispatcher;

		public CommandHandlerParameter Usage {
			get {
				var usage = new CommandHandlerParameter(
					"All",
					CommandType.Run,
					Command,
					"Writes a configuration setting in the current path (prints config if no arguments are specified)");
				usage.Add("list", "List available configuration options (*.oicfgoptions)");
				usage.Add("init", "Initializes a configuration point");
				var read = usage.Add("read", "Prints closest configuration or global if specified");
				read.Add("cfgfile", "Location of nearest configuration file");
				read.Add("cfgpoint", "Location of nearest configuration point");
				read.Add("rootpoint", "Location of current root location");
				read.Add("SETTING_NAME", "Setting to print the value of. Supports wildcard (global fallback)");
				read.Add("[--global]", "Forces configuration command to be directed towards global config");
				read.Add("[-g]", "Short version of --global");
				var setting = usage.Add("SETTING", "The statement to write to the config");
				setting.Add("[--global]", "Forces configuration command to be directed towards global config");
				setting.Add("[-g]", "Short version of --global");
				setting.Add("[--delete]", "Removes configuration setting");
				setting.Add("[-d]", "Short version of --delete");
				return usage;
			}
		}

		public string Command { get { return "conf"; } }

		public ConfigurationHandler(PluginLocator locator, Action<string> eventDispatcher) {
			_pluginLocator = locator;
			_eventDispatcher = eventDispatcher;
		}

		public void Execute(string[] arguments)
		{
			if (arguments.Length < 1)
				arguments = new[] { "readMerged" }; 
			
			var path = Environment.CurrentDirectory;

			if (arguments[0] == "init")
				initializingConfiguration(path);
			else if (arguments[0] == "list")
				printConfigurationOptions(path);
			else if (arguments[0] == "readMerged")
				printMergedConfig(path);
			else if (arguments[0] == "read")
				printClosestConfiguration(path, arguments);
			else
				updateConfiguration(path, arguments);
		}

		private void printMergedConfig(string path)
		{
			Console.WriteLine("Configuration for: " + path);
			Console.WriteLine();
			var reader = new ConfigReader(path);
			foreach (var key in reader.GetKeys()) {
				Console.WriteLine("\t{0}={1}", key, reader.Get(key));
			}
		}

		private void printConfigurationOptions(string path)
		{
			var file = new Configuration(path, true).ConfigurationFile;
			var paths = new List<string>();
			paths.Add(
				Path.Combine(
					Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
					"EditorEngine"));
			paths.Add(Path.GetDirectoryName(file));
			foreach (var plugin in _pluginLocator.Locate())
				paths.Add(plugin.GetPluginDir());
			var reader = new ConfigOptionsReader(paths.ToArray());
			reader.Parse();
			foreach (var line in reader.Options.OrderBy(x => x))
				Console.WriteLine(line);
		}

		private void updateConfiguration(string path, string[] arguments)
		{
			var args = parseArguments(arguments);
			if (args == null)
				return;

			if (args.Global)
				path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

			if (!Configuration.IsConfigured(path))
			{
				Console.WriteLine("There is no config point at " + path);
				return;
			}

			var config = new Configuration(path, false);
			if (args.Delete) {
				config.Delete(args.Settings[0]);
				_eventDispatcher(
					string.Format(
						"builtin configitem deleted \"{0}\" \"{1}\"",
						path,
						args.Settings[0]));
			} else {
				config.Write(args.Settings[0]);
				_eventDispatcher(
					string.Format(
						"builtin configitem updated \"{0}\" \"{1}\"",
						path,
						args.Settings[0]));
			}
		}

		private void initializingConfiguration(string path)
		{
			if (isInitialized(path))
				return;
			var dir = Path.Combine(path, ".OpenIDE");
			if (!Directory.Exists(dir))
				Directory.CreateDirectory(dir);
			File.WriteAllText(Path.Combine(dir, "oi.config"), "");
		}

		private bool isInitialized(string path)
		{
			var file = Path.Combine(path, ".OpenIDE");
			return Directory.Exists(file);
		}

		private void printClosestConfiguration(string path, string[] arguments)
		{
			var args = parseArguments(arguments);
			if (args == null)
				return;
			if (args.Global)
				path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

			var file = new Configuration(path, true).ConfigurationFile;
			if (!File.Exists(file))
				return;
			string pattern =  null;
			var wildcard = false;
			if (args.Settings.Length == 2)
				pattern = args.Settings[1];
			if (pattern == null) {
				Console.WriteLine("Configuration file: {0}", file);
				Console.WriteLine("");
				File.ReadAllLines(file).ToList()
					.ForEach(x => {
							Console.WriteLine("\t" + x);
						});
				return;
			}
			if (pattern == "cfgpoint") {
				Console.Write(Path.GetDirectoryName(file));
				return;
			}
			if (pattern == "rootpoint") {
				Console.Write(Path.GetDirectoryName(new ProfileLocator(path).GetLocalProfilesRoot()));
				return;
			}
			if (pattern == "cfgfile") {
				Console.Write(file);
				return;
			}
			if (pattern.EndsWith("*")) {
				wildcard = true;
				pattern = pattern.Substring(0, pattern.Length - 1);
			}
			if (wildcard) {
				foreach (var item in new ConfigReader(Path.GetDirectoryName(file)).GetStartingWith(pattern)) {
					Console.WriteLine("{0}={1}", item.Key, item.Value);
				}
			} else {
				Console.WriteLine(new ConfigReader(Path.GetDirectoryName(file)).Get(pattern));
			}
		}
		
		private CommandArguments parseArguments(string[] arguments)
		{
			var settings = arguments.Where(x => !x.StartsWith("-")).ToArray();
			if (settings.Length == 0)
			{
				Console.WriteLine("error|No argument provided");
				return null;
			}
			return new CommandArguments()
				{
					Settings = settings,
					Global = arguments.Contains("--global") || arguments.Contains("-g"),
					Delete = arguments.Contains("--delete") || arguments.Contains("-d")
				};
		}

		class CommandArguments
		{
			public string[] Settings { get; set; }
			public bool Global { get; set; }
			public bool Delete { get; set; }
		}
	}
}
