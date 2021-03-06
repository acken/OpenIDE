using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using OpenIDE.Core.Language;
using OpenIDE.Core.Profiles;
using OpenIDE.Core.Definitions;
using OpenIDE.Core.FileSystem;
using OpenIDE.Core.Scripts;
using OpenIDE.Core.Logging;

namespace OpenIDE.Core.Definitions
{
	public class BuiltInCommand
	{
		public string Name { get; private set; }
		public CommandHandlerParameter Usage { get; private set; }

		public BuiltInCommand(string name, CommandHandlerParameter usage) {
			Name = name;
			Usage = usage;
		}
	}

	public class DefinitionBuilder
	{
		private string _token;
		private string _workingDirectory;
		private string _defaultLanguage;
		private DefinitionCache _cache = new DefinitionCache();
		private Func<IEnumerable<BuiltInCommand>> _builtIn;
		private Func<string,IEnumerable<LanguagePlugin>> _languages;

		public IEnumerable<DefinitionCacheItem> Definitions { get { return _cache.Definitions; } }

		public DefinitionBuilder(
			string token,
			string workingDirectory,
			string defaultLanguage,
			Func<IEnumerable<BuiltInCommand>> builtIn,
			Func<string,IEnumerable<LanguagePlugin>> languages)

		{
			_token = token;
			_workingDirectory = workingDirectory;
			_defaultLanguage = defaultLanguage;
			_builtIn = builtIn;
			_languages = languages;
		}

		public DefinitionCacheItem Get(string[] args) {
			return _cache.Get(args);
		}

		public DefinitionCacheItem GetOriginal(string[] args) {
			return _cache.GetOriginal(args);
		}

		public DefinitionCacheItem GetBuiltIn(string[] args) {
			return _cache.GetBuiltIn(args);
		}
		
		public DefinitionCacheItem GetLanguage(string[] args) {
			return _cache.GetLanguage(args);
		}
		
		public DefinitionCacheItem GetLanguageScript(string[] args) {
			return _cache.GetLanguageScript(args);
		}

		public DefinitionCacheItem GetScript(string[] args) {
			return _cache.GetScript(args);
		}

		public void Build() {
			_cache = new DefinitionCache();
			var profiles = new ProfileLocator(_token);

			mergeBuiltInCommands(profiles);
			// Reverse paths to handle global first then local
			var paths = profiles.GetPathsCurrentProfiles().Reverse().ToArray();
			//for (int i = paths.Length - 1; i >= 0; i--)
			//	mergeExternalCommands(paths[i]);
			foreach (var path in paths)
				mergeExternalCommands(path);
		}

		private void mergeBuiltInCommands(ProfileLocator profiles) {
			var builtInFile = Path.Combine(profiles.AppRootPath, "oi-definitions.json");
			var builtInCache = new DefinitionCache();
			if (File.Exists(builtInFile)) {
				builtInCache = appendDefinitions(builtInFile);
				var updated = builtInCache.GetOldestItem();
				if (updated != null && fileTime(System.Reflection.Assembly.GetExecutingAssembly().Location) > updated.Updated)
					builtInCache = writeBuiltInCommands(builtInFile, profiles);
			} else {
				Logger.Write("Could not find definition file: " + builtInFile);
				builtInCache = writeBuiltInCommands(builtInFile, profiles);
			}
			_cache.Merge(builtInCache);
		}

		private void mergeExternalCommands(string path) {
			Logger.Write("Merging path " + path);
			var localCache = new DefinitionCache();
			var file = Path.Combine(path, "oi-definitions.json");
			if (File.Exists(file)) {
				localCache = appendDefinitions(file);
				if (cacheIsOutOfDate(file, localCache)) {
					Logger.Write("Definition file was out of date, updating " + file);
					localCache = buildDefinitions(file);
				}
			} else {
				Logger.Write("Could not find definition file: " + file);
				localCache = buildDefinitions(file);
			}
			
			_cache.Merge(localCache);
		}

		private DefinitionCache writeBuiltInCommands(string file, ProfileLocator profiles) {
			var builtInCache = new DefinitionCache();
			foreach (var cmd in _builtIn()) {
				var item = builtInCache	
					.Add(
						DefinitionCacheItemType.BuiltIn,
						null,
						DateTime.Now,
						false,
						true,
						cmd.Name,
						cmd.Usage.Description);
				add(builtInCache, item, cmd.Usage.Parameters);
			}
			writeCache(profiles.AppRootPath, builtInCache);
			return builtInCache;
		}

		private DateTime fileTime(string file) {
			var time = new FileInfo(file).LastWriteTime;
			var writeTime = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, time.Second);
			if (writeTime > DateTime.Now) {
				Logger.Write("Updating write time for " + file);
				File.SetLastWriteTime(file, DateTime.Now);
				writeTime = DateTime.Now;
			}
			return writeTime;
		}

		private void writeCache(string path, DefinitionCache cache) {
			new DefinitionCacheWriter(path)
				.Write(cache);
		}

		private DefinitionCache appendDefinitions(string file) {
			return new DefinitionCacheReader().Read(file);
		}

		private DefinitionCache buildDefinitions(string file) {
			var cache = new DefinitionCache();
			var dir = Path.GetDirectoryName(file);
			
			// Add languages
			var languagePath = Path.Combine(dir, "languages");
			LanguagePlugin defaultLanguage = null;
			foreach (var language in _languages(languagePath)) {
				var item = cache.Add(
					DefinitionCacheItemType.Language,
					language.FullPath,
					DateTime.Now,
					false,
					true,
					language.GetLanguage(),
					"Commands for the " + language.GetLanguage() + " plugin");
				add(cache, item, language.GetUsages());

				var languageScriptPath = 
					Path.Combine(
						Path.Combine(
							languagePath, language.GetLanguage() + "-files"),
							"scripts");
				Logger.Write("Adding scripts from " + languageScriptPath);
				var languageScripts = new ScriptFilter().GetScripts(languageScriptPath);
				foreach (var scriptFile in languageScripts) {
					Logger.Write("Script " + scriptFile);
					var script  = new Script(_token, _workingDirectory, scriptFile);
					var usages = script.Usages; // Description is built when fetching usages
					var scriptItem = item.Append(
						DefinitionCacheItemType.LanguageScript,
						scriptFile,
						DateTime.Now,
						false,
						true,
						script.Name,
						script.Description);
					add(cache, scriptItem, usages);
				}

				if (language.GetLanguage() == _defaultLanguage)
					defaultLanguage = language;
			}

			// Add scripts
			var scriptPath = Path.Combine(dir, "scripts");
			Logger.Write("Adding scripts from " + scriptPath);
			var scripts = new ScriptFilter().GetScripts(scriptPath);
			foreach (var scriptFile in scripts) {
				Logger.Write("Adding script " + scriptPath);
				var script  = new Script(_token, _workingDirectory, scriptFile);
				var usages = script.Usages; // Description is built when fetching usages
				var item = cache.Add(
					DefinitionCacheItemType.Script,
					scriptFile,
					DateTime.Now,
					false,
					true,
					script.Name,
					script.Description);
				add(cache, item, usages);
			}

			// Add default language
			if (defaultLanguage != null) {
				var parameters = cache.Get(new[] { defaultLanguage.GetLanguage() }).Parameters;
				foreach (var usage in parameters) {
					// Don't override existing commands with default language
					if (cache.Get(new[] { usage.Name }) == null) {
						var item = cache.Add(
							usage.Type,
							usage.Location,
							DateTime.Now,
							false,
							true,
							usage.Name,
							usage.Description);
						add(cache, item, usage.Parameters);
					}
				}
			}
			writeCache(dir, cache);
			return cache;
		}

		private void add(DefinitionCache cache, DefinitionCacheItem item, IEnumerable<DefinitionCacheItem> parameters) {
			foreach (var parameter in parameters)
				add(cache, item, parameter);
		}

		private void add(DefinitionCache cache, DefinitionCacheItem item, DefinitionCacheItem parameter) {
			var name = parameter.Name;
			var child =
				item.Append(
						item.Type,
						item.Location,
						item.Updated,
						parameter.Override,
						parameter.Required,
						name,
						parameter.Description);
			foreach (var cmd in parameter.Parameters)
				add(cache, child, cmd);
		}

		private void add(DefinitionCache cache, DefinitionCacheItem item, IEnumerable<BaseCommandHandlerParameter> parameters) {
			foreach (var parameter in parameters) {
				if (parameter.Override)
					overrideCommand(cache, item, parameter);
				else
					add(cache, item, parameter);
			}
		}

		private void add(DefinitionCache cache, DefinitionCacheItem item, BaseCommandHandlerParameter parameter) {
			var name = parameter.Name;
			var child =
				item.Append(
						item.Type,
						item.Location,
						item.Updated,
						parameter.Override,
						parameter.Required,
						name,
						parameter.Description);
			foreach (var cmd in parameter.Parameters)
				add(cache, child, cmd);
		}

		private void overrideCommand(DefinitionCache cache, DefinitionCacheItem item, BaseCommandHandlerParameter parameter) {
			var command = cache.Add(
				item.Type,
				item.Location,
				item.Updated,
				parameter.Override,
				parameter.Required,
				parameter.Name,
				parameter.Description);
			foreach (var cmd in parameter.Parameters)
				add(cache, command, cmd);
		}

		private bool cacheIsOutOfDate(string file, DefinitionCache cache) {
			try {
				var dir = Path.GetDirectoryName(file);
				var locations = cache.GetLocations(DefinitionCacheItemType.Script);
				var scriptPath = Path.Combine(dir, "scripts");
				var scripts = new ScriptFilter().GetScripts(scriptPath);
				if (scripts.Any(x => !locations.Contains(x))) {
					Logger.Write("New script has been added");
					return true;
				}
				if (locations.Any(x => !scripts.Contains(x))) {
					Logger.Write("New script has been added");
					return true;
				}
				
				foreach (var script in scripts) {
					if (isUpdated(script, cache))
						return true;
				}
				
				locations = cache.GetLocations(DefinitionCacheItemType.Language);
				var languagePath = Path.Combine(dir, "languages");
				var languages = _languages(languagePath);
				if (languages.Any(x => !locations.Contains(x.FullPath))) {
					Logger.Write("New language has been added");
					if (Logger.IsEnabled) {
						foreach (var newLanguage in languages.Where(x => !locations.Contains(x.FullPath)))
							Logger.Write("\t" + newLanguage.FullPath);
					}
					return true;
				}
				if (locations.Any(x => !languages.Any(y => y.FullPath == x))) {
					Logger.Write("Language has been removed");
					return true;
				}
				
				foreach (var language in languages) {
					if (isUpdated(language.FullPath, cache))
						return true;
					var languageScriptPath = 
						Path.Combine(
							Path.Combine(languagePath, language.GetLanguage() + "-files"),
							"scripts");
					if (Directory.Exists(languageScriptPath)) {
						locations = cache
							.GetLocations(DefinitionCacheItemType.LanguageScript)
							.Where(x => x.StartsWith(languageScriptPath))
							.ToArray();
						var languageScripts = new ScriptFilter().GetScripts(languageScriptPath);
						if (languageScripts.Any(x => !locations.Contains(x))) {
							Logger.Write("Language script has been added");
							return true;
						}
						if (locations.Any(x => !languageScripts.Contains(x))) {
							Logger.Write("Language script has been removed");
							return true;
						}
						foreach (var script in languageScripts) {
							if (isUpdated(script, cache))
								return true;
						}
					}
				}
			} catch (Exception ex) {
				Logger.Write(ex.ToString());
				return true;
			}
			return false;
		}

		private bool isUpdated(string file, DefinitionCache cache) {
			var updated = cache.GetOldestItem(file).Updated;
			var filetime = fileTime(file);
			if (filetime > updated) {
				Logger.Write("Oldest is {0} {1} for {2}", updated.ToShortDateString(), updated.ToLongTimeString(), file);
				Logger.Write("Definition is out of date for {0} with file time {1} {2}", file, filetime.ToShortDateString(), filetime.ToLongTimeString());
				return true;
			}

			var dir = Path.GetDirectoryName(file);
			var name = Path.GetFileNameWithoutExtension(file);
			var filesDir = Path.Combine(dir, name + "-files");
			if (Directory.Exists(filesDir)) {
				return isUpdated(updated, filesDir, Path.Combine(filesDir, "state"));
			}
			return false;
		}

		private bool isUpdated(DateTime updated, string dir, string stateDir) {
			foreach (var file in Directory.GetFiles(dir)) {
				var filetime = fileTime(file);
				if (filetime > updated) {
					Logger.Write("Oldest is {0} {1} for {2}", updated.ToShortDateString(), updated.ToLongTimeString(), file);
					Logger.Write("Definition is out of date for {0} with file time {1} {2}", file, filetime.ToShortDateString(), filetime.ToLongTimeString());
					return true;
				}
			}
			foreach (var subDir in Directory.GetDirectories(dir)) {
				if (subDir == stateDir)
					continue;
				if (isUpdated(updated, subDir, stateDir))
					return true;
			}
			return false;
		}
	}
}