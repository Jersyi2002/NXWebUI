using NXOpen;
using NXOpen.Features;
using NXOpen.UF;
using NXOpen.Utilities;

namespace NxWebUITool
{
    /// <summary>
    /// One-shot part setup: nested feature groups for 前排/后排 plus a
    /// user expression 厚度=5 mm. Idempotent — existing groups/expressions
    /// are reused rather than duplicated.
    /// </summary>
    public static class ProjectInit
    {
        public const string ThicknessName = "厚度";
        public const string ThicknessValue = "5";

        public static readonly ProjectGroup[] Tree =
        {
            new ProjectGroup("前排", new[] { "主驾驶建模", "副驾驶建模", "花纹", "画框" }),
            new ProjectGroup("后排", new[] { "建模", "花纹", "画框" }),
        };

        public static void Run()
        {
            var session = Session.GetSession();
            var part = session.Parts.Work;
            if (part == null)
            {
                Write(session, "初始化项目失败：请先打开或新建一个零件。");
                return;
            }

            var uf = UFSession.GetUFSession();
            var lockStatus = uf.Ui.LockUgAccess(UFConstants.UF_UI_FROM_CUSTOM);
            Session.UndoMarkId mark = 0;
            var marked = false;
            try
            {
                mark = session.SetUndoMark(Session.MarkVisibility.Visible, "初始化项目");
                marked = true;

                foreach (var group in Tree)
                    EnsureParent(part, uf, group);

                HideMembersAndDeleteStrays(session, part, uf);
                EnsureThickness(part);

                session.UpdateManager.DoUpdate(mark);
                Write(session, BuildSummary(part));
            }
            catch (Exception ex)
            {
                if (marked)
                {
                    try { session.UndoToMark(mark, null); }
                    catch { /* keep the original error */ }
                }
                Write(session, "初始化项目失败：\n" + ex);
            }
            finally
            {
                if (lockStatus == UFConstants.UF_UI_LOCK_SET)
                {
                    try { uf.Ui.UnlockUgAccess(UFConstants.UF_UI_FROM_CUSTOM); }
                    catch { /* ignore */ }
                }
            }
        }

        // hide_state=1 embeds/hides members inside the set. 0 shows them as
        // extra top-level feature groups in the Part Navigator.
        const int HideMembers = 1;

        static void EnsureParent(Part part, UFSession uf, ProjectGroup spec)
        {
            var parent = FindGroup(part, spec.Name);
            var owned = OwnedMemberTags(part);
            var children = new Feature[spec.Children.Length];
            for (var i = 0; i < spec.Children.Length; i++)
            {
                var child = FindMember(parent, spec.Children[i])
                    ?? FindUnownedGroup(part, spec.Children[i], owned);
                if (child == null)
                    child = CreateGroup(uf, spec.Children[i], null, HideMembers);
                children[i] = child;
                owned.Add(child.Tag);
            }

            if (parent == null)
            {
                parent = CreateGroup(uf, spec.Name, children, HideMembers);
            }
            else
            {
                SetMembers(uf, parent, children);
                HideSetMembers(uf, parent);
            }
        }

        static FeatureGroup CreateGroup(UFSession uf, string name, Feature[] members, int hideState)
        {
            Tag[] tags = null;
            var count = 0;
            if (members != null && members.Length > 0)
            {
                tags = new Tag[members.Length];
                for (var i = 0; i < members.Length; i++)
                    tags[i] = members[i].Tag;
                count = tags.Length;
            }

            Tag tag;
            uf.Modl.CreateSetOfFeature(name, tags, count, hideState, out tag);
            if (tag == Tag.Null)
                throw new InvalidOperationException("无法创建特征组：" + name);

            var created = NXObjectManager.Get(tag) as FeatureGroup;
            if (created == null)
                throw new InvalidOperationException("创建结果不是特征组：" + name);

            try { created.SetName(name); }
            catch { /* NX may already have assigned a unique name */ }
            HideSetMembers(uf, created);
            return created;
        }

        static void SetMembers(UFSession uf, FeatureGroup parent, Feature[] children)
        {
            if (parent == null || children == null || children.Length == 0) return;
            var tags = new Tag[children.Length];
            for (var i = 0; i < children.Length; i++)
                tags[i] = children[i].Tag;
            uf.Modl.EditSetMembers(parent.Tag, tags, tags.Length);
            HideSetMembers(uf, parent);
        }

        static void HideSetMembers(UFSession uf, FeatureGroup set)
        {
            if (set == null) return;
            var hide = HideMembers;
            uf.Modl.EditSetHideState(set.Tag, ref hide);
        }

        static void HideMembersAndDeleteStrays(Session session, Part part, UFSession uf)
        {
            foreach (var spec in Tree)
            {
                var parent = FindGroup(part, spec.Name);
                if (parent != null) HideSetMembers(uf, parent);
            }

            var owned = OwnedMemberTags(part);
            var toDelete = new List<NXObject>();
            foreach (Feature feature in part.Features)
            {
                var group = feature as FeatureGroup;
                if (group == null || IsParentName(group)) continue;
                if (!IsKnownChildName(group)) continue;
                if (owned.Contains(group.Tag)) continue;

                Feature[] members;
                group.GetMembers(out members);
                if (members == null || members.Length == 0)
                    toDelete.Add(group);
            }

            if (toDelete.Count > 0)
            {
                try { session.UpdateManager.AddObjectsToDeleteList(toDelete.ToArray()); }
                catch { /* leave extras rather than fail init */ }
            }
        }

        static bool IsKnownChildName(FeatureGroup group)
        {
            foreach (var spec in Tree)
            {
                foreach (var childName in spec.Children)
                {
                    if (NamesEqual(group, childName)) return true;
                }
            }
            return false;
        }

        static HashSet<Tag> OwnedMemberTags(Part part)
        {
            var owned = new HashSet<Tag>();
            foreach (var spec in Tree)
            {
                var parent = FindGroup(part, spec.Name);
                if (parent == null) continue;
                Feature[] members;
                parent.GetMembers(out members);
                if (members == null) continue;
                foreach (var member in members)
                {
                    if (member != null) owned.Add(member.Tag);
                }
            }
            return owned;
        }

        static FeatureGroup FindUnownedGroup(Part part, string name, HashSet<Tag> owned)
        {
            foreach (Feature feature in part.Features)
            {
                var group = feature as FeatureGroup;
                if (group == null || (owned != null && owned.Contains(group.Tag)))
                    continue;
                if (IsParentName(group)) continue;
                if (NamesEqual(group, name))
                    return group;
            }
            return null;
        }

        static bool IsParentName(FeatureGroup group)
        {
            foreach (var spec in Tree)
            {
                if (NamesEqual(group, spec.Name)) return true;
            }
            return false;
        }

        static FeatureGroup FindGroup(Part part, string name)
        {
            foreach (Feature feature in part.Features)
            {
                var group = feature as FeatureGroup;
                if (group != null && NamesEqual(group, name))
                    return group;
            }
            return null;
        }

        static FeatureGroup FindMember(FeatureGroup parent, string name)
        {
            if (parent == null) return null;
            Feature[] members;
            parent.GetMembers(out members);
            if (members == null) return null;
            foreach (var member in members)
            {
                var group = member as FeatureGroup;
                if (group != null && NamesEqual(group, name))
                    return group;
            }
            return null;
        }

        static bool NamesEqual(Feature feature, string name)
        {
            if (NameMatches(feature.Name, name)) return true;
            try { return NameMatches(feature.GetFeatureName(), name); }
            catch { return false; }
        }

        static bool NameMatches(string actual, string expected)
        {
            if (string.IsNullOrEmpty(actual) || string.IsNullOrEmpty(expected))
                return false;
            if (string.Equals(actual, expected, StringComparison.Ordinal))
                return true;
            var prefix = expected + "_";
            if (!actual.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            var suffix = actual.Substring(prefix.Length);
            if (suffix.Length == 0) return false;
            for (var i = 0; i < suffix.Length; i++)
            {
                if (suffix[i] < '0' || suffix[i] > '9') return false;
            }
            return true;
        }

        static void EnsureThickness(Part part)
        {
            var mm = FindMillimeter(part);
            var current = FindExpression(part, ThicknessName);
            var formula = ThicknessName + "=" + ThicknessValue;
            if (current != null)
            {
                if (mm != null)
                    part.Expressions.EditWithUnits(current, mm, ThicknessValue);
                else
                    part.Expressions.Edit(current, ThicknessValue);
                return;
            }

            if (mm != null)
                part.Expressions.CreateWithUnits(formula, mm);
            else
                part.Expressions.Create(formula);
        }

        static Expression FindExpression(Part part, string name)
        {
            foreach (Expression expression in part.Expressions)
            {
                if (string.Equals(expression.Name, name, StringComparison.Ordinal))
                    return expression;
            }
            try { return part.Expressions.FindObject(name); }
            catch { return null; }
        }

        static Unit FindMillimeter(Part part)
        {
            try
            {
                var named = part.UnitCollection.FindObject("MilliMeter");
                if (named != null) return named;
            }
            catch { /* fall through */ }

            try
            {
                foreach (var unit in part.UnitCollection.GetMeasureTypes("Length"))
                {
                    if (unit == null) continue;
                    if (string.Equals(unit.Symbol, "mm", StringComparison.OrdinalIgnoreCase))
                        return unit;
                    if (string.Equals(unit.TypeName, "MilliMeter", StringComparison.OrdinalIgnoreCase))
                        return unit;
                }
            }
            catch { /* unitless fallback */ }
            return null;
        }

        static string BuildSummary(Part part)
        {
            var lines = new List<string> { "初始化项目完成：" };
            foreach (var spec in Tree)
            {
                var parent = FindGroup(part, spec.Name);
                lines.Add("  父组 " + spec.Name + (parent != null ? " ✓" : " ✗"));
                foreach (var childName in spec.Children)
                {
                    var child = parent != null ? FindMember(parent, childName) : null;
                    lines.Add("    子组 " + childName + (child != null ? " ✓" : " ✗"));
                }
            }
            var thickness = FindExpression(part, ThicknessName);
            var shown = thickness != null
                ? thickness.RightHandSide
                : "?";
            lines.Add("  表达式 " + ThicknessName + "=" + shown + (thickness != null ? " ✓" : " ✗"));
            return string.Join("\n", lines);
        }

        static void Write(Session session, string text)
        {
            try
            {
                session.ListingWindow.Open();
                foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                    session.ListingWindow.WriteLine(line);
            }
            catch
            {
                /* NX listing not ready */
            }
        }

        public sealed class ProjectGroup
        {
            public ProjectGroup(string name, string[] children)
            {
                Name = name;
                Children = children;
            }

            public string Name { get; }
            public string[] Children { get; }
        }
    }
}
