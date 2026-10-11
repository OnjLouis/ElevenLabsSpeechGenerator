using System.Collections.Generic;

namespace ElevenLabsSpeechGenerator
{
    internal enum CatalogPageMove { Refresh, Next, Previous }

    internal sealed class CatalogPagination
    {
        private readonly List<string> previous = new List<string>();
        private string context;
        private string current = "";
        private string next = "";
        public bool CanPrevious(string scope) { return context == scope && previous.Count > 0; }
        public bool CanNext(string scope) { return context == scope && next.Length > 0; }
        public string Request(CatalogPageMove move, string scope)
        {
            if (move == CatalogPageMove.Refresh || context != scope) return "";
            if (move == CatalogPageMove.Previous) return CanPrevious(scope) ? previous[previous.Count - 1] : null;
            return CanNext(scope) ? next : null;
        }
        public void Commit(CatalogPageMove move, string scope, string page, string following)
        {
            // Commit only after a successful response, so failed requests keep the current page usable.
            if (move == CatalogPageMove.Refresh || context != scope) previous.Clear();
            else if (move == CatalogPageMove.Previous) previous.RemoveAt(previous.Count - 1);
            else previous.Add(current);
            context = scope; current = page; next = following ?? "";
            if (next == current || previous.Contains(next)) next = "";
        }
    }
}
