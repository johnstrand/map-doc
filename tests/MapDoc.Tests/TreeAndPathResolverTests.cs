using MapDoc.Core.Models;
using MapDoc.Core.Validation;

namespace MapDoc.Tests;

public class TreeAndPathResolverTests
{
    [Fact]
    public void SelfReferentialType_ExpandsLazily_AndResolvesDeepPaths()
    {
        var empId = Guid.NewGuid();
        var pName = new PropertyDefinition { Id = Guid.NewGuid(), OwnerTypeId = empId, Name = "FullName", Type = TypeDescriptor.ScalarString(), SortOrder = 0 };
        var pMgr = new PropertyDefinition { Id = Guid.NewGuid(), OwnerTypeId = empId, Name = "Manager", Type = TypeDescriptor.ObjectOf(empId), SortOrder = 1 };
        var empType = new TypeDefinition { Id = empId, Name = "Employee", Properties = [pName, pMgr] };

        var catalog = new Dictionary<Guid, TypeDefinition> { [empId] = empType };

        // Unexpanded tree will not infinitely recurse; Manager is flagged IsRecursiveCycle
        var collapsedTree = PropertyPathResolver.BuildTree(empType, catalog, new HashSet<string>());
        Assert.Equal(2, collapsedTree.Count);
        var mgrNode = collapsedTree.Single(n => n.Name == "Manager");
        Assert.True(mgrNode.CanExpand);
        Assert.False(mgrNode.IsExpanded);
        Assert.True(mgrNode.IsRecursiveCycle);
        Assert.Empty(mgrNode.Children);

        // Deep path Employee -> Manager -> Manager -> FullName resolves cleanly
        var deepPath = $"{pMgr.Id}/{pMgr.Id}/{pName.Id}";
        var resolved = PropertyPathResolver.Resolve(empType, deepPath, catalog);
        Assert.True(resolved.IsValid);
        Assert.Equal("Manager.Manager.FullName", resolved.DisplayPath);
        Assert.Equal(BaseTypeKind.String, resolved.Type.BaseKind);

        // Expanding ancestors expands the exact requested depth
        var expandedSet = PropertyPathResolver.GetAncestorPathsToExpand([deepPath]);
        var expandedTree = PropertyPathResolver.BuildTree(empType, catalog, expandedSet);
        var flat = PropertyPathResolver.FlattenVisible(expandedTree);
        Assert.Contains(flat, n => n.Path == deepPath && n.DisplayPath == "Manager.Manager.FullName");
    }

    [Fact]
    public void ScalarArray_ExpandsToSyntheticItem_AndCollapsedAncestorRollsUp()
    {
        var pTags = new PropertyDefinition
        {
            Id = Guid.NewGuid(),
            Name = "TagIds",
            Type = TypeDescriptor.ArrayOf(TypeDescriptor.ScalarString(StringSubtype.Uuid), elementIsNullable: true)
        };
        var root = new TypeDefinition { Id = Guid.NewGuid(), Name = "Doc", Properties = [pTags] };
        var catalog = new Dictionary<Guid, TypeDefinition> { [root.Id] = root };

        var itemPath = $"{pTags.Id}/{PropertyPathResolver.ArrayItemToken}";
        var resolved = PropertyPathResolver.Resolve(root, itemPath, catalog);
        Assert.True(resolved.IsValid);
        Assert.Equal("TagIds[]", resolved.DisplayPath);
        Assert.Equal(1, resolved.ArrayDepth);
        Assert.Equal(StringSubtype.Uuid, resolved.Type.StringSubtype);
        Assert.True(resolved.LeafIsNullable);

        // When TagIds is collapsed, FindVisibleOrAncestorPath rolls up to TagIds
        var collapsedVisible = new HashSet<string> { pTags.Id.ToString() };
        var (visPath, rolledUp) = PropertyPathResolver.FindVisibleOrAncestorPath(itemPath, collapsedVisible);
        Assert.Equal(pTags.Id.ToString(), visPath);
        Assert.True(rolledUp);
    }
}
