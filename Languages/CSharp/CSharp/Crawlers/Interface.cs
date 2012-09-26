using System;
using CSharp.Crawlers.TypeResolvers;
using CSharp.Projects;
namespace CSharp.Crawlers
{
	public class Interface : ICodeReference 
	{
        public bool AllTypesAreResolved { get; private set; }

		public string Type { get; private set; }
        public FileRef File { get; private set; }
		public string Signature { get { return string.Format("{0}.{1}", Namespace, Name); } }
		public string Namespace { get; private set; }
		public string Name { get; private set; }
        public string Scope { get; private set; }
		public int Line { get; private set; }
		public int Column { get; private set; }
        public string JSON { get; private set; }

        public Interface(FileRef file, string ns, string name, string scope, int line, int column, string json)
		{
			File = file;
			Namespace = ns;
			Name = name;
            Scope = scope;
			Line = line;
			Column = column;
            JSON = json;
		}

        public string GenerateFullSignature() {
            return null;
        }

        public void ResolveTypes(ICacheReader cache) {
            throw new NotImplementedException();
        }
	}
}

