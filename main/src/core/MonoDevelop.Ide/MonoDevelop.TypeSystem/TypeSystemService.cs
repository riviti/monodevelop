// 
// TypeSystemService.cs
//  
// Author:
//       Mike Krüger <mkrueger@novell.com>
// 
// Copyright (c) 2011 Mike Krüger <mkrueger@novell.com>
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO; 
using MonoDevelop.Projects;
using Mono.Cecil;
using ICSharpCode.NRefactory.TypeSystem.Implementation;
using ICSharpCode.NRefactory.TypeSystem;
using Mono.Addins;
using MonoDevelop.Core;
using MonoDevelop.Ide;
using Mono.TextEditor;
using System.Threading;
using MonoDevelop.Core.ProgressMonitoring;
using MonoDevelop.Core.Collections;
using System.Xml;
using ICSharpCode.NRefactory.Utils;
using ICSharpCode.NRefactory;

namespace MonoDevelop.TypeSystem
{
	public static class TypeSystemServiceExt
	{
		public static Project GetProject (this IProjectContent content)
		{
			return TypeSystemService.GetProject (content.AssemblyName);
		}
		
		public static Project GetSourceProject (this ITypeDefinition type)
		{
			return TypeSystemService.GetProject (type.Compilation.MainAssembly.AssemblyName);
		}
		
		public static Project GetSourceProject (this IType type)
		{
			return type.GetDefinition ().GetSourceProject ();
		}
		
		public static IProjectContent GetProjectContent (this IType type)
		{
			return TypeSystemService.GetProjectContext (type.GetSourceProject ());
		}
		
		public static TextLocation GetLocation (this IType type)
		{
			return type.GetDefinition ().Region.Begin;
		}
		
		public static string GetDocumentation (this IType type)
		{
			return "TODO";
		}
		
		public static bool IsBaseType (this IType type, IType potentialBase)
		{
			return type.GetAllBaseTypes ().Any (t => t.Equals (potentialBase));
		}
		
		public static bool IsObsolete (this IEntity member)
		{
			// TODO: Implement me!
			return false;
		}
		
		public static bool IsObsolete (this IUnresolvedEntity member)
		{
			// TODO: Implement me!
			return false;
		}
		
		public static IType Resolve (this IUnresolvedTypeDefinition def, Project project)
		{
			var pf = TypeSystemService.GetProjectContext (project).GetFile (def.Region.FileName);
			var ctx = pf.GetTypeResolveContext (TypeSystemService.GetCompilation (project), def.Region.Begin);
			return def.Resolve (ctx);
		}
		
		public static ITypeDefinition LookupType (this ICompilation compilation, string ns, string name, int typeParameterCount = 0)
		{
			var result = compilation.MainAssembly.GetTypeDefinition (ns, name, typeParameterCount);
			if (result != null)
				return result;
			foreach (var refAsm in compilation.ReferencedAssemblies) {
				result = refAsm.GetTypeDefinition (ns, name, typeParameterCount);
				if (result != null)
					return result;
			}
			return null;
		}
		
		public static ITypeDefinition LookupType (this ICompilation compilation, string fullName, int typeParameterCount = 0)
		{
			int idx = fullName.LastIndexOf ('.');
			string ns, name;
			if (idx > 0) {
				ns = fullName.Substring (0, idx);
				name = fullName.Substring (idx + 1);
			} else {
				ns = "";
				name = fullName;
			}
			return compilation.LookupType (ns, name, typeParameterCount);
		}
	}
	
	public static class TypeSystemService
	{
		static List<TypeSystemParserNode> parsers;
		
		static IEnumerable<TypeSystemParserNode> Parsers {
			get {
				if (parsers == null) {
//					Counters.ParserServiceInitialization.BeginTiming ();
					parsers = new List<TypeSystemParserNode> ();
					AddinManager.AddExtensionNodeHandler ("/MonoDevelop/TypeSystem/Parser", delegate (object sender, ExtensionNodeEventArgs args) {
						switch (args.Change) {
						case ExtensionChange.Add:
							parsers.Add ((TypeSystemParserNode)args.ExtensionNode);
							break;
						case ExtensionChange.Remove:
							parsers.Remove ((TypeSystemParserNode)args.ExtensionNode);
							break;
						}
					});
//					Counters.ParserServiceInitialization.EndTiming ();
				}
				return parsers;
			}
		}
		
		static ITypeSystemParser GetParser (string mimeType)
		{
			var provider = Parsers.FirstOrDefault (p => p.CanParse (mimeType));
			return provider != null ? provider.Parser : null;
		}
		
		public static ParsedDocument ParseFile (Project project, string fileName)
		{
			return ParseFile (project, fileName, DesktopService.GetMimeTypeForUri (fileName), File.ReadAllText (fileName));
		}
		
		public static ParsedDocument ParseFile (Project project, string fileName, string mimeType, TextReader content)
		{
			ProjectContentWrapper wrapper;
			if (project != null) {
				projectContents.TryGetValue (project, out wrapper);
			} else {
				wrapper = null;
			}
			
			var parser = GetParser (mimeType);
			if (parser == null)
				return null;
			try {
				var result = parser.Parse (true, fileName, content);
				if (wrapper != null && (result.Flags & ParsedDocumentFlags.NonSerializable) != ParsedDocumentFlags.NonSerializable)
					wrapper.Content = wrapper.Content.UpdateProjectContent (wrapper.Content.GetFile (fileName), result);
				return result;
			} catch (Exception e) {
				LoggingService.LogError ("Exception while parsing :" + e);
				return new ParsedDocument (fileName) { Flags = ParsedDocumentFlags.NonSerializable };
			}
		}
		
		public static ParsedDocument ParseFile (Project project, string fileName, string mimeType, string content)
		{
			using (var reader = new StringReader (content))
				return ParseFile (project, fileName, mimeType, reader);
		}
		
		public static ParsedDocument ParseFile (Project project, TextEditorData data)
		{
			return ParseFile (project, data.FileName, data.MimeType, data.Text);
		}
		
		
		
		public static ParsedDocument ParseFile (string fileName, string mimeType, string text, ProjectContentWrapper wrapper = null)
		{
			using (var reader = new StringReader (text))
				return ParseFile (fileName, mimeType, reader, wrapper);
		}
		
		public static ParsedDocument ParseFile (string fileName, string mimeType, TextReader content, ProjectContentWrapper wrapper = null)
		{
			var parser = GetParser (mimeType);
			if (parser == null)
				return null;
			try {
				var result = parser.Parse (true, fileName, content);
				if (wrapper != null && (result.Flags & ParsedDocumentFlags.NonSerializable) != ParsedDocumentFlags.NonSerializable)
					wrapper.Content = wrapper.Content.UpdateProjectContent (wrapper.Content.GetFile (fileName), result);
				return result;
			} catch (Exception e) {
				LoggingService.LogError ("Exception while parsing :" + e);
				return new ParsedDocument (fileName) { Flags = ParsedDocumentFlags.NonSerializable };
			}
		}
		
		public static event EventHandler ParseOperationStarted;
		
		internal static void StartParseOperation ()
		{
			if ((parseStatus++) == 0) {
				if (ParseOperationStarted != null)
					ParseOperationStarted (null, EventArgs.Empty);
			}
		}
		
		public static event EventHandler ParseOperationFinished;
		
		internal static void EndParseOperation ()
		{
			if (parseStatus == 0)
				return;
			if (--parseStatus == 0) {
				if (ParseOperationFinished != null)
					ParseOperationFinished (null, EventArgs.Empty);
			}
		}
		#region Parser Database Handling

		static string GetWorkspacePath (string dataPath)
		{
			try {
				if (!File.Exists (dataPath))
					return null;
				using (var reader = XmlReader.Create (dataPath)) {
					while (reader.Read ()) {
						if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "File")
							return reader.GetAttribute ("name");
					}
				}
			} catch (Exception e) {
				LoggingService.LogError ("Error while reading derived data file " + dataPath, e);
			}
			return null;
		}
		
		static string GetCacheDirectory (string fileName)
		{
			string derivedDataPath = UserProfile.Current.CacheDir.Combine ("DerivedData");
		
			string[] subDirs;
			
			try {
				if (!Directory.Exists (derivedDataPath))
					return null;
				subDirs = Directory.GetDirectories (derivedDataPath);
			} catch (Exception e) {
				LoggingService.LogError ("Error while getting derived data directories.", e);
				return null;
			}
			
			foreach (var subDir in subDirs) {
				var dataPath = Path.Combine (subDir, "data.xml");
				var workspacePath = GetWorkspacePath (dataPath);
				if (workspacePath == fileName) 
					return subDir;
			}
			return null;
		}
		
		static string GetName (string baseName, int i)
		{
			if (i == 0)
				return baseName;
			return baseName + "-" + i;
		}
		
		static string CreateCacheDirectory (string fileName)
		{
			try {
				string derivedDataPath = UserProfile.Current.CacheDir.Combine ("DerivedData");
				string name = Path.GetFileNameWithoutExtension (fileName);
				string baseName = Path.Combine (derivedDataPath, name);
				int i = 0;
				while (Directory.Exists (GetName (baseName, i)))
					i++;
				
				string cacheDir = GetName (baseName, i);
				
				Directory.CreateDirectory (cacheDir);
				
				System.IO.File.WriteAllText (Path.Combine(cacheDir, "data.xml"), string.Format ("<DerivedData><File name=\"{0}\"/></DerivedData>", fileName));
				return cacheDir;
			} catch (Exception e) {
				LoggingService.LogError ("Error creating cache for " + fileName, e);
				return null;
			}
		}
		
		static T DeserializeObject<T> (string path) where T : class
		{
			try {
				using (var fs = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan)) {
					using (var reader = new BinaryReaderWith7BitEncodedInts (fs)) {
						var s = new FastSerializer();
						return (T)s.Deserialize (reader);
					}
				}
			} catch (Exception e) {
				LoggingService.LogError ("Error while reading type system cache.", e);
				return default(T);
			}
		}
		
		static void SerializeObject (string path, object obj)
		{
			try {
				using (var fs = new FileStream (path, FileMode.Create, FileAccess.Write)) {
					using (var writer = new BinaryWriterWith7BitEncodedInts (fs)) {
						FastSerializer s = new FastSerializer ();
						s.Serialize (writer, obj);
					}
				}
			} catch (Exception e) {
				LoggingService.LogError ("Error while writing type system cache.", e);
			}
		}
		
		static Dictionary<string, IProjectContent> solutionCache = new Dictionary<string, IProjectContent> ();
		
		/// <summary>
		/// Removes all cache directories which are older than 30 days.
		/// </summary>
		static void CleanupCache ()
		{
			string derivedDataPath = UserProfile.Current.CacheDir.Combine ("DerivedData");
			string[] subDirs;
			
			try {
				if (!Directory.Exists (derivedDataPath))
					return;
				subDirs = Directory.GetDirectories (derivedDataPath);
			} catch (Exception e) {
				LoggingService.LogError ("Error while getting derived data directories.", e);
				return;
			}
			
			foreach (var subDir in subDirs) {
				try {
					var days = Math.Abs ((DateTime.Now - Directory.GetLastWriteTime (subDir)).TotalDays);
					if (days > 30)
						Directory.Delete (subDir, true);
				} catch (Exception e) {
					LoggingService.LogError ("Error while removing outdated cache " + subDir, e);
				}
			}
		}
		
		static void RemoveCache (string cacheDir)
		{
			try {
				Directory.Delete (cacheDir, true);
			} catch (Exception e) {
				LoggingService.LogError ("Error while removing cache " + cacheDir, e);
			}
		}
		
		static void TouchCache (string cacheDir)
		{
			try {
				Directory.SetLastWriteTime (cacheDir, DateTime.Now);
			} catch (Exception e) {
				LoggingService.LogError ("Error while touching cache directory " + cacheDir, e);
			}
		}
		
		static void LoadSolutionCache (Solution solution)
		{
			string cacheDir = GetCacheDirectory (solution.FileName);
			if (cacheDir == null) {
				solutionCache = new Dictionary<string, IProjectContent> ();
				return;
			}
			TouchCache (cacheDir);
			solutionCache = DeserializeObject<Dictionary<string, IProjectContent>> (Path.Combine (cacheDir, "completion.cache")) ?? new Dictionary<string, IProjectContent> ();
			if (solutionCache == null)
 				RemoveCache (cacheDir);
			CleanupCache ();
		}
		
		static void StoreSolutionCache (Solution solution)
		{
			string cacheDir = GetCacheDirectory (solution.FileName) ?? CreateCacheDirectory (solution.FileName);
			TouchCache (cacheDir);
			
			var cache = new Dictionary<string, ProjectContentWrapper> ();
			foreach (var pair in projectContents) {
				var key = pair.Key.FileName.ToString ();
				if (string.IsNullOrEmpty (key))
					continue;
				cache[key] = pair.Value;
			}
			
			string fileName = Path.GetTempFileName ();
			
			SerializeObject (fileName, cache);
			
			string cacheFile = Path.Combine (cacheDir, "completion.cache");
			
			try {
				if (File.Exists (cacheFile))
					System.IO.File.Delete (cacheFile);
				
				System.IO.File.Move (fileName, cacheFile);
			} catch (Exception e) {
				LoggingService.LogError ("Error whil saving cache " + cacheFile, e);
			}
		}
		#endregion
		
		#region Project loading
		public static void Load (WorkspaceItem item)
		{
			if (item is Workspace) {
				var ws = (Workspace)item;
				foreach (WorkspaceItem it in ws.Items)
				Load (it);
				ws.ItemAdded += OnWorkspaceItemAdded;
				ws.ItemRemoved += OnWorkspaceItemRemoved;
			} else if (item is Solution) {
				var solution = (Solution)item;
				LoadSolutionCache (solution);
				foreach (Project project in solution.GetAllProjects ())
					Load (project);
				ReloadAllReferences ();
				solution.SolutionItemAdded += OnSolutionItemAdded;
				solution.SolutionItemRemoved += OnSolutionItemRemoved;
			}
		}
		
		static void ReloadAllReferences ()
		{
			lock (rwLock) {
				foreach (var wrapper in projectContents)
					wrapper.Value.ReloadAssemblyReferences (wrapper.Key);
			}
		}
		
		[Serializable]
		public class ProjectContentWrapper
		{
			IProjectContent content;
			public IProjectContent Content {
				get {
					return content;
				}
				set {
					content = value;
					compilation = null;
				}
			}
			
			[NonSerialized]
			ICompilation compilation = null;
			
			public ICompilation Compilation {
				get {
					if (compilation == null)
						compilation = Content.CreateCompilation ();
					return compilation;
				}
			}
			
			public Project Project {
				get {
					foreach (var pair in projectContents){
						if (pair.Value == this)
							return pair.Key;
					}
					return null;
				}
			}
			
			public ProjectContentWrapper (IProjectContent content)
			{
				this.content = content;
			}
			
			
			public IEnumerable<Project> GetReferencedProjects  (Project project)
			{
				foreach (var pr in project.GetReferencedItems (ConfigurationSelector.Default)) {
					var referencedProject = pr as Project;
					if (referencedProject != null)
						yield return referencedProject;
				}
			}

			public void ReloadAssemblyReferences (Project project)
			{
				var netProject = project as DotNetProject;
				if (netProject == null)
					return;
				var contexts = new List<IAssemblyReference> ();
		
				foreach (var referencedProject in GetReferencedProjects (project)) {
					ProjectContentWrapper wrapper;
					if (projectContents.TryGetValue (referencedProject, out wrapper))
						contexts.Add (wrapper.Compilation.MainAssembly.UnresolvedAssembly);
				}
				
				AssemblyContext ctx;
				// Add mscorlib reference
				var corLibRef = netProject.TargetRuntime.AssemblyContext.GetAssemblyForVersion (typeof(object).Assembly.FullName, null, netProject.TargetFramework);
				ctx = LoadAssemblyContext (corLibRef.Location);
				if (ctx != null)
					contexts.Add (ctx.Ctx);
				
				// Get the assembly references throught the project, since it may have custom references
				foreach (string file in netProject.GetReferencedAssemblies (ConfigurationSelector.Default, false)) {
					string fileName;
					if (!Path.IsPathRooted (file)) {
						fileName = Path.Combine (Path.GetDirectoryName (netProject.FileName), file);
					} else {
						fileName = Path.GetFullPath (file);
					}
					ctx = LoadAssemblyContext (fileName);
					
					if (ctx != null)
						contexts.Add (ctx.Ctx);
				}
				
				Content = Content.RemoveAssemblyReferences (Content.AssemblyReferences);
				Content = Content.AddAssemblyReferences (contexts);
			}
		}
		
		static Dictionary<Project, ProjectContentWrapper> projectContents = new Dictionary<Project, ProjectContentWrapper> ();
		static Dictionary<Project, int> referenceCounter = new Dictionary<Project, int> ();
		
		static void Load (Project project)
		{
			if (IncLoadCount (project) != 1)
				return;
			lock (rwLock) {
				if (projectContents.ContainsKey (project))
					return;
				try {
					IProjectContent context = null;
					if (solutionCache.ContainsKey (project.FileName))
						context = solutionCache [project.FileName];
					
					ProjectContentWrapper wrapper;
					if (context == null) {
						context = new ICSharpCode.NRefactory.CSharp.CSharpProjectContent ();
						projectContents [project] = wrapper = new ProjectContentWrapper (context);
						QueueParseJob (projectContents [project], project);
					} else {
						projectContents [project] = wrapper = new ProjectContentWrapper (context);
					}
					
					referenceCounter [project] = 1;
					OnProjectContentLoaded (new ProjectContentEventArgs (project, context));
					project.FileChangedInProject += OnFileChanged;
					project.FileAddedToProject += OnFileAdded;
					project.FileRemovedFromProject += OnFileRemoved;
					project.FileRenamedInProject += OnFileRenamed;
					project.Modified += OnProjectModified;
				} catch (Exception ex) {
					LoggingService.LogError ("Parser database for project '" + project.Name + " could not be loaded", ex);
				}
			}
		}
		
		public static Project GetProject (IEntity entity)
		{
			if (entity == null)
				return null;
			
			ITypeDefinition def;
			if (entity is IType) {
				def = ((IType)entity).GetDefinition ();
			} else {
				def = entity.DeclaringTypeDefinition;
			}
			if (def == null)
				return null;
			return GetProject (def.Compilation.MainAssembly.AssemblyName);
				
		}
		
		public static Project GetProject (string assemblyName)
		{
			foreach (var wrapper in projectContents) 
				if (wrapper.Value.Compilation.MainAssembly.AssemblyName == assemblyName)
					return wrapper.Key;
			return null;
		}
		
		#region Project modification handlers
		static void OnFileChanged (object sender, ProjectFileEventArgs args)
		{
			var project = (Project)sender;
			foreach (ProjectFileEventInfo fargs in args) {
				QueueParseJob (projectContents [project], project, new [] { fargs.ProjectFile });
			}
		}
		
		static void OnFileAdded (object sender, ProjectFileEventArgs args)
		{
			var project = (Project)sender;
			foreach (ProjectFileEventInfo fargs in args) {
				QueueParseJob (projectContents [project], project, new [] { fargs.ProjectFile });
			}
		}

		static void OnFileRemoved (object sender, ProjectFileEventArgs args)
		{
			var project = (Project)sender;
			foreach (ProjectFileEventInfo fargs in args) {
				projectContents [project].Content = projectContents [project].Content.UpdateProjectContent (projectContents [project].Content.GetFile (fargs.ProjectFile.Name), null);
			}
		}

		static void OnFileRenamed (object sender, ProjectFileRenamedEventArgs args)
		{
			var project = (Project)sender;
			foreach (ProjectFileRenamedEventInfo fargs in args) {
				projectContents [project].Content = projectContents [project].Content.UpdateProjectContent (projectContents [project].Content.GetFile (fargs.OldName), null);
				QueueParseJob (projectContents [project], project, new [] { fargs.ProjectFile });
			}
		}
		
		static void OnProjectModified (object sender, SolutionItemModifiedEventArgs args)
		{
			if (!args.Any (x => x is SolutionItemModifiedEventInfo && (((SolutionItemModifiedEventInfo)x).Hint == "TargetFramework" || ((SolutionItemModifiedEventInfo)x).Hint == "References")))
				return;
			cachedProjectContents = new Dictionary<Project, ITypeResolveContext> ();
			var project = (Project)sender;
			
			ProjectContentWrapper wrapper;
			projectContents.TryGetValue (project, out wrapper);
			if (wrapper == null)
				return;
			ReloadAllReferences ();
		}
		#endregion
		
		public static event EventHandler<ProjectContentEventArgs> ProjectContentLoaded;
		static void OnProjectContentLoaded (ProjectContentEventArgs e)
		{
			var handler = ProjectContentLoaded;
			if (handler != null)
				handler (null, e);
		}
		
		public static void Unload (WorkspaceItem item)
		{
			if (item is Workspace) {
				var ws = (Workspace)item;
				foreach (WorkspaceItem it in ws.Items)
					Unload (it);
				ws.ItemAdded -= OnWorkspaceItemAdded;
				ws.ItemRemoved -= OnWorkspaceItemRemoved;
			} else if (item is Solution) {
				Solution solution = (Solution)item;
				StoreSolutionCache (solution);
				foreach (var project in solution.GetAllProjects ()) {
					Unload (project);
				}
				solution.SolutionItemAdded -= OnSolutionItemAdded;
				solution.SolutionItemRemoved -= OnSolutionItemRemoved;
			}
		}
		
		public static void Unload (Project project)
		{
			if (DecLoadCount (project) != 0)
				return;
			
			if (--referenceCounter [project] <= 0) {
				project.FileChangedInProject   -= OnFileChanged;
				project.FileAddedToProject     -= OnFileAdded;
				project.FileRemovedFromProject -= OnFileRemoved;
				project.FileRenamedInProject   -= OnFileRenamed;
				project.Modified               -= OnProjectModified;
				
				projectContents.Remove (project);
				referenceCounter.Remove (project);
				
				OnProjectUnloaded (new ProjectEventArgs (project));
			}
		}
		
		public static event EventHandler<ProjectEventArgs> ProjectUnloaded;

		static void OnProjectUnloaded (ProjectEventArgs e)
		{
			var handler = ProjectUnloaded;
			if (handler != null)
				handler (null, e);
		}
		
		static void OnWorkspaceItemAdded (object s, WorkspaceItemEventArgs args)
		{
			Load (args.Item);
		}
		
		static void OnWorkspaceItemRemoved (object s, WorkspaceItemEventArgs args)
		{
			Unload (args.Item);
		}
		
		static void OnSolutionItemAdded (object sender, SolutionItemChangeEventArgs args)
		{
			if (args.SolutionItem is Project)
				Load ((Project)args.SolutionItem);
		}
		
		static void OnSolutionItemRemoved (object sender, SolutionItemChangeEventArgs args)
		{
			if (args.SolutionItem is Project)
				Unload ((Project)args.SolutionItem);
		}
		#endregion

		#region Reference Counting
		static Dictionary<object,int> loadCount = new Dictionary<object,int> ();
		static object rwLock = new object ();
		
		static int DecLoadCount (object ob)
		{
			lock (rwLock) {
				int c;
				if (loadCount.TryGetValue (ob, out c)) {
					c--;
					if (c == 0)
						loadCount.Remove (ob);
					else
						loadCount [ob] = c;
					return c;
				}
				LoggingService.LogError ("DecLoadCount: Object not registered.");
				return 0;
			}
		}
		
		static int IncLoadCount (object ob)
		{
			lock (rwLock) {
				int c;
				if (loadCount.TryGetValue (ob, out c)) {
					c++;
					loadCount [ob] = c;
					return c;
				} else {
					loadCount [ob] = 1;
					return 1;
				}
			}
		}
		#endregion
		
		
		class SimpleAssemblyResolver : IAssemblyResolver
		{
			string lookupPath;
			Dictionary<string, AssemblyDefinition> cache = new Dictionary<string, AssemblyDefinition> ();
			DefaultAssemblyResolver defaultResolver = new DefaultAssemblyResolver ();
			
			public SimpleAssemblyResolver (string lookupPath)
			{
				this.lookupPath = lookupPath;
			}

			public AssemblyDefinition InternalResolve (string fullName)
			{
				AssemblyDefinition result;
				if (cache.TryGetValue (fullName, out result))
					return result;
				
				var name = AssemblyNameReference.Parse (fullName);
				// need to handle different file extension casings. Some dlls from windows tend to end with .Dll or .DLL rather than '.dll'
				foreach (string file in Directory.GetFiles (lookupPath, name.Name + ".*")) {
					string ext = Path.GetExtension (file);
					if (string.IsNullOrEmpty (ext))
						continue;
					ext = ext.ToUpper ();
					if (ext == ".DLL" || ext == ".EXE") {
						result = ReadAssembly (file);
						break;
					}
				}
				
				if (result == null) {
					var framework = MonoDevelop.Projects.Services.ProjectService.DefaultTargetFramework;
					var assemblyName = Runtime.SystemAssemblyService.DefaultAssemblyContext.GetAssemblyFullName (fullName, framework);
					var location = Runtime.SystemAssemblyService.DefaultAssemblyContext.GetAssemblyLocation (assemblyName, framework);
					
					if (!string.IsNullOrEmpty (location) && File.Exists (location)) {
						result = ReadAssembly (location);
					}
				}
				if (result != null)
					cache [fullName] = result;
				return result;
			}

			#region IAssemblyResolver implementation
			public AssemblyDefinition Resolve (AssemblyNameReference name)
			{
				return InternalResolve (name.FullName) ?? defaultResolver.Resolve (name);
			}

			public AssemblyDefinition Resolve (AssemblyNameReference name, ReaderParameters parameters)
			{
				return InternalResolve (name.FullName) ?? defaultResolver.Resolve (name, parameters);
			}

			public AssemblyDefinition Resolve (string fullName)
			{
				return InternalResolve (fullName) ?? defaultResolver.Resolve (fullName);
			}

			public AssemblyDefinition Resolve (string fullName, ReaderParameters parameters)
			{
				return InternalResolve (fullName) ?? defaultResolver.Resolve (fullName, parameters);
			}
			#endregion
		}
		
		static AssemblyDefinition ReadAssembly (string fileName)
		{
			ReaderParameters parameters = new ReaderParameters ();
			parameters.AssemblyResolver = new DefaultAssemblyResolver (); // new SimpleAssemblyResolver (Path.GetDirectoryName (fileName));
			using (var stream = new MemoryStream (File.ReadAllBytes (fileName))) {
				return AssemblyDefinition.ReadAssembly (stream, parameters);
			}
		}
		
		static bool GetXml (string baseName, out FilePath xmlFileName)
		{
			string filePattern = Path.GetFileNameWithoutExtension (baseName) + ".*";
			try {
				foreach (string fileName in Directory.EnumerateFileSystemEntries (Path.GetDirectoryName (baseName), filePattern)) {
					if (fileName.ToLower ().EndsWith (".xml")) {
						xmlFileName = fileName;
						return true;
					}
				}
			} catch (Exception e) {
				LoggingService.LogError ("Error while retrieving file system entries.", e);
			}
			xmlFileName = "";
			return false;
		}
		
		[Serializable]
		class AssemblyContext
		{
			public string FileName;
			public DateTime LastWriteTime;
			public IUnresolvedAssembly Ctx;
		}
		
		static AssemblyContext LoadAssemblyContext (string fileName)
		{
			AssemblyContext loadedContext;
			if (cachedAssemblyContents.TryGetValue(fileName, out loadedContext))
				return loadedContext;
			
			string cache = GetCacheDirectory (fileName);
			if (cache != null) {
				TouchCache (cache);
				var deserialized = DeserializeObject <AssemblyContext> (Path.Combine (cache, "completion.cache"));
				if (deserialized != null) {
					cachedAssemblyContents[fileName] = deserialized;
					return deserialized;
				} else {
					RemoveCache (cache);
				}
			}
			
			var asm = ReadAssembly (fileName);
			if (asm == null)
				return null;
			
			var loader = new CecilLoader ();
			FilePath xmlDocFile;
			if (GetXml (fileName, out xmlDocFile)) {
				try {
					loader.DocumentationProvider = new ICSharpCode.NRefactory.Documentation.XmlDocumentationProvider (xmlDocFile);
				} catch (Exception ex) {
					LoggingService.LogWarning ("Ignoring error while reading xml doc from " + xmlDocFile, ex);
				}
			}
//			loader.InterningProvider = new SimpleInterningProvider ();
			try {
				var result = new AssemblyContext ();
				result.FileName = fileName;
				result.LastWriteTime = File.GetLastWriteTime (fileName);
				result.Ctx = loader.LoadAssembly (asm);
				cache = CreateCacheDirectory (fileName);
				if (cache != null)
					SerializeObject (Path.Combine (cache, "completion.cache"), result);
				cachedAssemblyContents[fileName] = result;
				return result;
			} catch (Exception ex) {
				LoggingService.LogError ("Error loading assembly " + fileName, ex);
				return null;
			}
		}
		
		public static ICompilation LoadAssemblyContext (MonoDevelop.Core.Assemblies.TargetRuntime runtime, string fileName)
		{ // TODO: Runtimes
/*			var asm = ReadAssembly (fileName);
			if (asm == null)
				return null;
			var result = new CecilLoader ().LoadAssembly (asm);
			return result;*/
			return null;
		}
		
		public static IProjectContent GetProjectContext (Project project)
		{
			if (project == null)
				return null;
			ProjectContentWrapper content;
			projectContents.TryGetValue (project, out content);
			return content.Content;
		}
		
		public static ICompilation GetCompilation (Project project)
		{
			if (project == null)
				return null;
			ProjectContentWrapper content;
			projectContents.TryGetValue (project, out content);
			return content.Compilation;
		}
		
		public static ProjectContentWrapper GetProjectContentWrapper (Project project)
		{
			if (project == null)
				return null;
			ProjectContentWrapper content;
			projectContents.TryGetValue (project, out content);
			return content;
		}
		
		public static IProjectContent GetContext (FilePath file, string mimeType, string text)
		{
			using (var reader = new StringReader (text)) {
				var parsedFile = ParseFile (file, mimeType, reader);
				
				var content = new ICSharpCode.NRefactory.CSharp.CSharpProjectContent ();
				return content.UpdateProjectContent (null, parsedFile);
			}
		}
		
		static Dictionary<Project, ITypeResolveContext> cachedProjectContents = new Dictionary<Project, ITypeResolveContext> ();
		static Dictionary<string, AssemblyContext> cachedAssemblyContents = new Dictionary<string, AssemblyContext> ();
		
		public static void ForceUpdate (ProjectContentWrapper context)
		{
			CheckModifiedFiles ();
		}
		
		#region Parser queue
		static bool threadRunning;
		
		public static IProgressMonitorFactory ParseProgressMonitorFactory {
			get;
			set;
		}
		
			
		class InternalProgressMonitor: NullProgressMonitor
		{
			public InternalProgressMonitor ()
			{
				StartParseOperation ();
			}
			
			public override void Dispose ()
			{
				EndParseOperation ();
			}
		}

		internal static IProgressMonitor GetParseProgressMonitor ()
		{
			IProgressMonitor mon;
			if (ParseProgressMonitorFactory != null)
				mon = ParseProgressMonitorFactory.CreateProgressMonitor ();
			else
				mon = new NullProgressMonitor ();
			
			return new AggregatedProgressMonitor (mon, new InternalProgressMonitor ());
		}
		
		static Queue<ParsingJob> parseQueue = new Queue<ParsingJob>();
		class ParsingJob
		{
			public ProjectContentWrapper Context;
			public Project Project;
			public IEnumerable<ProjectFile> FileList;
//			public Action<string, IProgressMonitor> ParseCallback;
			
			public void Run (IProgressMonitor monitor)
			{
				foreach (var file in (FileList ?? Project.Files)) {
					if (!string.Equals (file.BuildAction, "compile", StringComparison.OrdinalIgnoreCase)) 
						continue;
					
					var parser = TypeSystemService.GetParser (DesktopService.GetMimeTypeForUri (file.FilePath));
					if (parser == null)
						continue;
					using (var stream = new System.IO.StreamReader (file.FilePath)) {
						var parsedFile = parser.Parse (false, file.FilePath, stream);
						Context.Content = Context.Content.UpdateProjectContent (Context.Content.GetFile (file.FilePath), parsedFile);
					}
//					if (ParseCallback != null)
//						ParseCallback (file.FilePath, monitor);
				}
			}
		}
		static object parseQueueLock = new object ();
		static AutoResetEvent parseEvent = new AutoResetEvent (false);
		static ManualResetEvent queueEmptied = new ManualResetEvent (true);
		static bool trackingFileChanges;
		
		public static bool TrackFileChanges {
			get {
				return trackingFileChanges;
			}
			set {
				lock (parseQueueLock) {
					if (value != trackingFileChanges) {
						trackingFileChanges = value;
						if (value)
							StartParserThread ();
					}
				}
			}
		}
		
		static int parseStatus;
		
		public static bool IsParsing {
			get { return parseStatus > 0; }
		}
		
		static Dictionary<Project, ParsingJob> parseQueueIndex = new Dictionary<Project,ParsingJob>();
		internal static int PendingJobCount {
			get {
				lock (parseQueueLock) {
					return parseQueueIndex.Count;
				}
			}
		}
		
		static void QueueParseJob (ProjectContentWrapper context, /* Action<string, IProgressMonitor> callback,*/ Project project, IEnumerable<ProjectFile> fileList = null)
		{
			var job = new ParsingJob () {
				Context = context,
//				ParseCallback = callback,
				Project = project,
				FileList = fileList
			};
			
			lock (parseQueueLock)
			{
				RemoveParseJob (project);
				parseQueueIndex [project] = job;
				parseQueue.Enqueue (job);
				parseEvent.Set ();
				
				if (parseQueueIndex.Count == 1)
					queueEmptied.Reset ();
			}
		}
		
		static bool WaitForParseJob (int timeout)
		{
			return parseEvent.WaitOne (5000, true);
		}
		
		static ParsingJob DequeueParseJob ()
		{
			lock (parseQueueLock)
			{
				if (parseQueue.Count > 0) {
					var job = parseQueue.Dequeue ();
					parseQueueIndex.Remove (job.Project);
					return job;
				}
				return null;
			}
		}
		
		internal static void WaitForParseQueue ()
		{
			queueEmptied.WaitOne ();
		}
		
		static void RemoveParseJob (Project project)
		{
			lock (parseQueueLock)
			{
				ParsingJob job;
				if (parseQueueIndex.TryGetValue (project, out job)) {
					parseQueueIndex.Remove (project);
				}
			}
		}
		
		static void RemoveParseJobs (IProjectContent context)
		{
			lock (parseQueueLock)
			{
				foreach (var pj in parseQueue) {
					if (pj.Context == context) {
						parseQueueIndex.Remove (pj.Project);
					}
				}
			}
		}
		
		static void StartParserThread()
		{
			lock (parseQueueLock) {
				if (!threadRunning) {
					threadRunning = true;
					var t = new Thread (new ThreadStart (ParserUpdateThread));
					t.Name = "Background parser";
					t.IsBackground  = true;
					t.Priority = ThreadPriority.AboveNormal;
					t.Start ();
				}
			}
		}
		
		static void ParserUpdateThread()
		{
			try {
				while (trackingFileChanges) {
					if (!WaitForParseJob (5000))
						CheckModifiedFiles ();
					else if (trackingFileChanges)
						ConsumeParsingQueue ();
				}
			} catch (Exception ex) {
				LoggingService.LogError ("Unhandled error in parsing thread", ex);
			}
			lock (parseQueueLock) {
				threadRunning = false;
				if (trackingFileChanges)
					StartParserThread ();
			}
		}

		static bool IsFileModified (ProjectFile file, IParsedFile parsedFile)
		{
			if (parsedFile == null)
				return true;
			return System.IO.File.GetLastWriteTime (file.FilePath) > parsedFile.LastWriteTime;
		}

		static void CheckModifiedFiles (Project project, ProjectContentWrapper content)
		{
			List<ProjectFile> modifiedFiles = null;
			foreach (var file in project.Files) {
				if (!string.Equals (file.BuildAction, "compile", StringComparison.OrdinalIgnoreCase)) 
					continue;
					
				var parser = TypeSystemService.GetParser (DesktopService.GetMimeTypeForUri (file.FilePath));
				if (parser == null)
					continue;
				
				if (!IsFileModified (file, content.Content.GetFile (file.FilePath)))
					continue;
				if (modifiedFiles == null)
					modifiedFiles = new List<ProjectFile> ();
				modifiedFiles.Add (file);
			}
			
			// check if file needs to be removed from project content 
			foreach (var file in content.Content.Files) {
				if (project.GetProjectFile (file.FileName) == null)
					content.Content = content.Content.UpdateProjectContent (file, null);
			}
			
			if (modifiedFiles == null)
				return;
			QueueParseJob (content, project, modifiedFiles);
		}

		static void CheckModifiedFile (AssemblyContext context)
		{
			try {
				var writeTime = File.GetLastWriteTime (context.FileName);
				if (writeTime != context.LastWriteTime) {
					string cache = GetCacheDirectory (context.FileName);
					context.Ctx = new CecilLoader ().LoadAssembly (ReadAssembly (context.FileName));
					context.LastWriteTime = writeTime;
					if (cache != null) {
						SerializeObject (Path.Combine (cache, "completion.cache"), context);
					}
					ReloadAllReferences ();
				}
			} catch (Exception e) {
				LoggingService.LogError ("Error while updating assembly " + context.FileName, e);
			}
		}
		
		static void CheckModifiedFiles ()
		{
			Queue<KeyValuePair<Project, ProjectContentWrapper>> list;
			
			lock (rwLock) {
				list = new Queue<KeyValuePair<Project, ProjectContentWrapper>> (projectContents);
			}
			
			while (list.Count > 0) {
				var readydb = list.Dequeue ();
				CheckModifiedFiles (readydb.Key, readydb.Value);
			}
			
			Queue<KeyValuePair<string, AssemblyContext>> assemblyList;
			
			lock (rwLock) {
				assemblyList = new Queue<KeyValuePair<string, AssemblyContext>> (cachedAssemblyContents);
			}
			
			while (assemblyList.Count > 0) {
				var readydb = assemblyList.Dequeue ();
				CheckModifiedFile (readydb.Value);
			}
		}
		
		static void ConsumeParsingQueue ()
		{
			int pending = 0;
			IProgressMonitor monitor = null;
			
			try {
				do {
					if (pending > 5 && monitor == null) {
						monitor = GetParseProgressMonitor ();
						monitor.BeginTask (GettextCatalog.GetString ("Generating database"), 0);
					}
					var job = DequeueParseJob ();
					if (job != null) {
						try {
							job.Run (monitor);
						} catch (Exception ex) {
							if (monitor == null)
								monitor = GetParseProgressMonitor ();
							monitor.ReportError (null, ex);
						}
					}
					
					pending = PendingJobCount;
					
				} while (pending > 0);
				ReloadAllReferences ();
				queueEmptied.Set ();
			} finally {
				if (monitor != null)
					monitor.Dispose ();
			}
		}
		#endregion
	}
}
