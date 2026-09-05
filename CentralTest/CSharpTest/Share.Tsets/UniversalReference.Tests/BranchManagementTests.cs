
using CentralConsole.Testing;
using CentralConsole.Testing.Attributes;
using CentralConsole.Testing.Builders;
using CentralConsole.Testing.Core;
using CentralConsole.Testing.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.BranchManagement.Managers;
using UniversalWorkflowFramework.BranchManagement.Models;
using UniversalWorkflowFramework.Storage.Local;

namespace BasicMethod.Tests.UniversalReference;

[TestClass]
public class BranchManagementTests : UniversalReferenceTestBase
{
    [TestMethod]
    public async Task BranchManagement_AllTests()
    {
        var result = await new TestGroupBuilder("BranchManagement")
            .WithIsolatedDir("RegisterBranch_ShouldReturnValidId", async (dir) =>
            {
                var storage = LocalStorageFactory.Create<BranchGraph>(dir);
                var manager = BranchManagerFactory.Create<BranchGraph>(storage);
                await manager.InitializeAsync();

                var branch = UniversalReferenceTestDataFactory.CreateUserRegistrationBranch();
                var branchId = await manager.RegisterBranchAsync("user-registration", branch);
                return !string.IsNullOrEmpty(branchId);
            })
            .WithIsolatedDir("GetBranch_ShouldReturnRegisteredBranch", async (dir) =>
            {
                var storage = LocalStorageFactory.Create<BranchGraph>(dir);
                var manager = BranchManagerFactory.Create<BranchGraph>(storage);
                await manager.InitializeAsync();

                var branch = UniversalReferenceTestDataFactory.CreateUserRegistrationBranch();
                var branchId = await manager.RegisterBranchAsync("user-registration", branch);

                var retrieved = await manager.GetBranchAsync(branchId);
                return retrieved != null && retrieved.Name == "用户注册流程";
            })
            .WithIsolatedDir("BranchExists_ShouldReturnTrueForExistingBranch", async (dir) =>
            {
                var storage = LocalStorageFactory.Create<BranchGraph>(dir);
                var manager = BranchManagerFactory.Create<BranchGraph>(storage);
                await manager.InitializeAsync();

                var branch = UniversalReferenceTestDataFactory.CreateUserRegistrationBranch();
                var branchId = await manager.RegisterBranchAsync("user-registration", branch);

                return await manager.BranchExistsAsync(branchId);
            })
            .WithIsolatedDir("GetAllBranchIds_ShouldReturnCorrectCount", async (dir) =>
            {
                var storage = LocalStorageFactory.Create<BranchGraph>(dir);
                var manager = BranchManagerFactory.Create<BranchGraph>(storage);
                await manager.InitializeAsync();

                await manager.RegisterBranchAsync("branch-1", UniversalReferenceTestDataFactory.CreateUserRegistrationBranch());
                await manager.RegisterBranchAsync("branch-2", UniversalReferenceTestDataFactory.CreateOrderProcessingBranch());

                var allIds = await manager.GetAllBranchIdsAsync();
                return allIds.Count == 2;
            })
            .WithIsolatedDir("RemoveBranch_ShouldDecreaseCount", async (dir) =>
            {
                var storage = LocalStorageFactory.Create<BranchGraph>(dir);
                var manager = BranchManagerFactory.Create<BranchGraph>(storage);
                await manager.InitializeAsync();

                var id1 = await manager.RegisterBranchAsync("branch-1", UniversalReferenceTestDataFactory.CreateUserRegistrationBranch());
                var id2 = await manager.RegisterBranchAsync("branch-2", UniversalReferenceTestDataFactory.CreateOrderProcessingBranch());

                await manager.RemoveBranchAsync(id1);
                await manager.RemoveBranchAsync(id2);

                var allIds = await manager.GetAllBranchIdsAsync();
                return allIds.Count == 0;
            })
            .WithIsolatedDir("ValidateBranch_ShouldReturnValidResult", async (dir) =>
            {
                var storage = LocalStorageFactory.Create<BranchGraph>(dir);
                var manager = BranchManagerFactory.Create<BranchGraph>(storage);
                await manager.InitializeAsync();

                var branch = UniversalReferenceTestDataFactory.CreateUserRegistrationBranch();
                var branchId = await manager.RegisterBranchAsync("user-registration", branch);

                var validation = await manager.ValidateBranchAsync(branchId);
                return validation != null;
            })
            .RunAsync();

        result.AssertAllPassed(printSummary: true);
    }
}
