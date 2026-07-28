using FluentAssertions;
using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Exceptions;
using InsuranceManagement.Infrastructure.Services;

namespace InsuranceManagement.Tests;

public class CustomerServiceTests
{
    [Fact]
    public async Task CreateAsync_OnboardsCustomer_WithGeneratedId()
    {
        await using var db = TestDb.CreateContext();
        var sut = new CustomerService(db);

        var created = await sut.CreateAsync(NewCustomer("Alice", "alice@example.com"));

        created.Id.Should().NotBeEmpty();
        created.Email.Should().Be("alice@example.com");
        (await db.Customers.FindAsync(created.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_WhenEmailExists_ThrowsConflict()
    {
        await using var db = TestDb.CreateContext();
        var sut = new CustomerService(db);
        await sut.CreateAsync(NewCustomer("Alice", "alice@example.com"));

        var act = async () => await sut.CreateAsync(NewCustomer("Other", "ALICE@example.com"));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task GetAllAsync_WithSearch_FiltersByNameOrEmail()
    {
        await using var db = TestDb.CreateContext();
        var sut = new CustomerService(db);
        await sut.CreateAsync(NewCustomer("Alice Johnson", "alice@example.com"));
        await sut.CreateAsync(NewCustomer("Bob Smith", "bob@example.com"));

        var byName = await sut.GetAllAsync("alice", page: 1, pageSize: 10);
        var byEmail = await sut.GetAllAsync("bob@", page: 1, pageSize: 10);

        byName.Items.Should().ContainSingle(c => c.FullName == "Alice Johnson");
        byEmail.Items.Should().ContainSingle(c => c.Email == "bob@example.com");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsPagedEnvelope()
    {
        await using var db = TestDb.CreateContext();
        var sut = new CustomerService(db);
        await sut.CreateAsync(NewCustomer("A", "a@example.com"));
        await sut.CreateAsync(NewCustomer("B", "b@example.com"));
        await sut.CreateAsync(NewCustomer("C", "c@example.com"));

        var page = await sut.GetAllAsync(page: 2, pageSize: 2, sortBy: "fullName", sortDir: "asc");

        page.Page.Should().Be(2);
        page.PageSize.Should().Be(2);
        page.TotalItems.Should().Be(3);
        page.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetByIdAsync_WhenMissing_ThrowsNotFound()
    {
        await using var db = TestDb.CreateContext();
        var sut = new CustomerService(db);

        var act = async () => await sut.GetByIdAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_UpdatesCustomerDetails()
    {
        await using var db = TestDb.CreateContext();
        var sut = new CustomerService(db);
        var created = await sut.CreateAsync(NewCustomer("Alice", "alice@example.com"));

        var updated = await sut.UpdateAsync(created.Id, NewCustomer("Alice Updated", "alice.new@example.com"));

        updated.FullName.Should().Be("Alice Updated");
        updated.Email.Should().Be("alice.new@example.com");
    }

    [Fact]
    public async Task UpdateAsync_WhenEmailExists_ThrowsConflict()
    {
        await using var db = TestDb.CreateContext();
        var sut = new CustomerService(db);
        var customerA = await sut.CreateAsync(NewCustomer("Alice", "alice@example.com"));
        var customerB = await sut.CreateAsync(NewCustomer("Bob", "bob@example.com"));

        var act = async () => await sut.UpdateAsync(customerB.Id, NewCustomer("Bob Updated", customerA.Email));

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task UpdateAsync_WhenMissing_ThrowsNotFound()
    {
        await using var db = TestDb.CreateContext();
        var sut = new CustomerService(db);

        var act = async () => await sut.UpdateAsync(Guid.NewGuid(), NewCustomer("Nobody", "nobody@example.com"));

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*was not found*");
    }

    private static Customer NewCustomer(string name, string email) => new()
    {
        FullName = name,
        Email = email,
        Phone = "+1-555-0100",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Address = "1 Test Street"
    };
}
