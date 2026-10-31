using Amazon.CognitoIdentityProvider.Model;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GuestSessions;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.GuestSessions;
using AutoFixture.Xunit2;
using Moq;
using NPOI.POIFS.Storage;
using System.Security.Cryptography;
using System.Text;

namespace APIHandler.Tests.Services;

public class GuestSessionServiceTests
{
    private readonly Mock<IGuestSessionRepository> _mockGuestSessionRepository = new();
    private readonly Mock<IGroupReservationRepository> _mockGroupReservationRepository = new();
    private readonly Mock<IDishRepository> _mockDishRepository = new();
    private readonly Mock<IReservationRepository> _mockReservationRepository = new();
    private readonly Mock<IUserRepository> _mockUserRepository = new();
    private readonly Mock<ITableRepository> _mockTableRepository = new();
    private readonly Mock<IOrderRepository> _mockOrderRepository = new();
    private readonly GuestSessionService _guestSessionService;

    public GuestSessionServiceTests()
    {
        _guestSessionService = new GuestSessionService(
            _mockGroupReservationRepository.Object,
            _mockGuestSessionRepository.Object,
            _mockDishRepository.Object,
            _mockReservationRepository.Object,
            _mockUserRepository.Object,
            _mockTableRepository.Object,
            _mockOrderRepository.Object);
    }
    [Theory]
    [InlineAutoData(0)]
    [InlineAutoData(3)]
    public async Task CreateGuestSessionAsync_CorrectData_ReturnsSessionToken(
        int guestCount,
        CreateGuestSessionRequest request,
        GroupReservation groupReservation,
        Reservation parentReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;

        parentReservation.GuestCount = 5;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(parentReservation.Id))
            .ReturnsAsync(parentReservation);

        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([]);

        GuestSession? session = null;
        _mockGuestSessionRepository
            .Setup(r => r.SaveAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(s => session = s);

        _mockGuestSessionRepository
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(guestCount);

        var response = await _guestSessionService.CreateGuestSessionAsync(request);

        Assert.NotEmpty(response.SessionId);
        Assert.NotEmpty(response.SessionToken);
        Assert.NotNull(session);
        Assert.Equal(groupReservation.GroupReservationId, session.GroupReservationId);
        Assert.Equal(request.GuestName, session.GuestName);
        Assert.NotNull(session.SessionTokenHash);
        Assert.NotEmpty(session.SessionTokenHash);
    }

    [Theory]
    [AutoData]
    public async Task CreateGuestSessionAsync_InvalidGroupReservationId_ThrowsGroupReservationNotFoundException(CreateGuestSessionRequest request)
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(request.GroupReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _guestSessionService.CreateGuestSessionAsync(request));
    }

    [Theory]
    [AutoData]
    public async Task CreateGuestSessionAsync_GroupReservationExpired_ThrowsGroupReservationExpiredException(CreateGuestSessionRequest request, GroupReservation groupReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(request.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _guestSessionService.CreateGuestSessionAsync(request));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldAddNewDish_WhenDishDoesNotExistInSession(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, Dish dish)
    {
        guestSession.Dishes = [];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;
        dish.State = DishState.Available;
        int quantityToAdd = 2;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);

        await _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantityToAdd);

        Assert.Single(guestSession.Dishes);
        Assert.Equal(dish.Id, guestSession.Dishes[0].DishId);
        Assert.Equal(quantityToAdd, guestSession.Dishes[0].Quantity);
        Assert.Equal(OrderItemState.Pending, guestSession.Dishes[0].State);

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(guestSession), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldIncreaseQuantity_WhenDishAlreadyExists(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, Dish dish)
    {
        int initialQuantity = 3;
        int quantityToAdd = 2;
        guestSession.Dishes = [new OrderItem { DishId = dish.Id, Quantity = initialQuantity, State = OrderItemState.Pending }];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;
        dish.State = DishState.Available;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);

        await _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantityToAdd);

        Assert.Single(guestSession.Dishes);
        Assert.Equal(initialQuantity + quantityToAdd, guestSession.Dishes[0].Quantity);

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(guestSession), Times.Once);
    }

    [Theory]
    [InlineAutoData(0)]
    [InlineAutoData(-1)]
    public async Task AddDishAsync_ShouldThrowValidationException_WhenQuantityIsZeroOrLess(int quantity, string guestSessionId, Dish dish)
    {
        dish.State = DishState.Available;

        _mockDishRepository
            .Setup(r => r.GetDishByIdAsync(dish.Id))
            .ReturnsAsync(dish);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.AddDishAsync(guestSessionId, dish.Id, quantity));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldThrowGuestSessionNotFoundException_WhenSessionNotFound(string guestSessionId, Dish dish)
    {
        dish.State = DishState.Available;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        _mockDishRepository
            .Setup(r => r.GetDishByIdAsync(dish.Id))
            .ReturnsAsync(dish);

        var exception = await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.AddDishAsync(guestSessionId, dish.Id, 2));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldThrowDishNotFound_WhenDishNotFound(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, string dishId)
    {
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);

        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([]);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.AddDishAsync(guestSession.Id, dishId, quantity: 2));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldThrowDishNotAvailable_WhenDishIsNotAvailable(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, Dish dish)
    {
        dish.State = DishState.OutOfStock;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);

        var exception = await Assert.ThrowsAsync<DishIsNotAvailableException>(() =>
            _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantity: 2));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    public async Task AddDishAsync_ShouldThrowGroupReservationExpired_WhenGroupStatusIsNotOpen(GroupReservationStatus status, Type exceptionType, GuestSession guestSession, GroupReservation groupReservation, Dish dish)
    {
        dish.State = DishState.Available;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = status;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockDishRepository
            .Setup(r => r.GetDishByIdAsync(dish.Id))
            .ReturnsAsync(dish);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        var exception = await Assert.ThrowsAsync(exceptionType, () =>
            _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantity: 2));
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldDecreaseQuantity_WhenQuantityIsGreaterThanAmountToRemove(GuestSession guestSession, GroupReservation groupReservation, string dishId)
    {
        int initialQuantity = 5;
        int quantityToRemove = 2;
        guestSession.Dishes =
        [
            new OrderItem { DishId = dishId, Quantity = initialQuantity, State = OrderItemState.Pending }
        ];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);


        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await _guestSessionService.RemoveDishAsync(guestSession.Id, dishId, quantityToRemove);

        Assert.Single(guestSession.Dishes);
        Assert.Equal(initialQuantity - quantityToRemove, guestSession.Dishes[0].Quantity);

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(guestSession), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldRemoveDishFromList_WhenQuantityToRemoveIsEqualOrGreater(GuestSession guestSession, GroupReservation groupReservation, string dishId)
    {
        int initialQuantity = 2;
        int quantityToRemove = 3;
        guestSession.Dishes =
        [
            new OrderItem { DishId = dishId, Quantity = initialQuantity, State = OrderItemState.Pending }
        ];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await _guestSessionService.RemoveDishAsync(guestSession.Id, dishId, quantityToRemove);

        Assert.Empty(guestSession.Dishes);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(guestSession), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldThrowDishNotFoundException_WhenDishDoesNotExistInSession(GuestSession guestSession, GroupReservation groupReservation, string dishId)
    {
        guestSession.Dishes = [];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
           .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
           .ReturnsAsync(groupReservation);

        var exception = await Assert.ThrowsAsync<DishNotFoundException>(() =>
            _guestSessionService.RemoveDishAsync(guestSession.Id, dishId, 2));

        Assert.Contains("not found in guest session", exception.Message);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldThrowValidationException_WhenQuantityIsZeroOrLess(string guestSessionId, string dishId)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.RemoveDishAsync(guestSessionId, dishId, 0));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.RemoveDishAsync(guestSessionId, dishId, -5));
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldThrowGuestSessionNotFoundException_WhenSessionNotFound(string guestSessionId, string dishId)
    {
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        var exception = await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.RemoveDishAsync(guestSessionId, dishId, 2));
    }
    [Theory]
    [InlineAutoData(1, 0)]
    [InlineAutoData(1, 1)]
    [InlineAutoData(2, 1)]
    [InlineAutoData(2, 2)]
    public async Task CreateGuestSessionAsync_WhenCapacityReached_ThrowsGuestLimitExceededException(
        int reservationCapacity,
        int guestCount,
        CreateGuestSessionRequest request,
        GroupReservation groupReservation,
        Reservation parentReservation)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.ReservationId = parentReservation.Id;

        parentReservation.GuestCount = reservationCapacity;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(parentReservation.Id))
            .ReturnsAsync(parentReservation);

        _mockGuestSessionRepository
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(guestCount);

        var exception = await Assert.ThrowsAsync<GuestLimitExceededException>(() => _guestSessionService.CreateGuestSessionAsync(request));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task CreateGuestSessionAsync_MissingParentReservation_ThrowsGroupReservationExpiredException(
        CreateGuestSessionRequest request,
        GroupReservation groupReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(groupReservation.ReservationId))
            .ReturnsAsync((Reservation?)null);

        var exception = await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _guestSessionService.CreateGuestSessionAsync(request));
    }

    [Theory]
    [AutoData]
    public async Task IsAuthorizedAsync_ValidTokenAndId_ReturnsTrue(GuestSession guestSession, byte[] tokenBytes)
    {
        string tokenString = Convert.ToBase64String(tokenBytes);
        var hashString = Convert.ToBase64String(SHA256.HashData(tokenBytes));
        guestSession.SessionTokenHash = hashString;

        _mockGuestSessionRepository
            .Setup(r => r.GetByTokenHash(hashString))
            .ReturnsAsync(guestSession);

        bool result = await _guestSessionService.IsAuthorizedAsync(tokenString, guestSession.Id);

        Assert.True(result);
    }

    [Theory]
    [AutoData]
    public async Task IsAuthorizedAsync_InvalidToken_ReturnsFalse(byte[] tokenBytes, string guestSessionId)
    {
        string tokenString = Convert.ToBase64String(tokenBytes);
        var hashString = Convert.ToBase64String(SHA256.HashData(tokenBytes));

        _mockGuestSessionRepository
            .Setup(r => r.GetByTokenHash(hashString))
            .ReturnsAsync((GuestSession?)null);

        bool result = await _guestSessionService.IsAuthorizedAsync(tokenString, guestSessionId);

        Assert.False(result);
    }

    [Theory]
    [AutoData]
    public async Task IsAuthorizedAsync_InvalidGuestSessionId_ReturnsFalse(GuestSession guestSession, byte[] tokenBytes, string guestSessionId)
    {
        string tokenString = Convert.ToBase64String(tokenBytes);
        var hashString = Convert.ToBase64String(SHA256.HashData(tokenBytes));
        guestSession.SessionTokenHash = hashString;

        _mockGuestSessionRepository
            .Setup(r => r.GetByTokenHash(hashString))
            .ReturnsAsync(guestSession);

        bool result = await _guestSessionService.IsAuthorizedAsync(tokenString, guestSessionId);

        Assert.False(result);
    }
    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_Valid_SubmitsSuccessfuilly(GuestSession guestSession, GroupReservation groupReservation, Dish[] dishes)
    {
        foreach (var dish in dishes)
            dish.State = DishState.Available;

        guestSession.IsSubmitted = false;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
           .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
           .ReturnsAsync(groupReservation);

        _mockDishRepository
            .Setup(d => d.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(dishes);

        GuestSession? session = null;
        _mockGuestSessionRepository
            .Setup(r => r.SaveAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(s => session = s);

        await _guestSessionService.SubmitCartAsync(guestSession.Id);

        Assert.NotNull(session);
        Assert.True(session.IsSubmitted);
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_SessionNotFound_ThrowsGuestSessionNotFound(string guestSessionId)
    {
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _guestSessionService.SubmitCartAsync(guestSessionId));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    public async Task SubmitCartAsync_GroupReservationNotOpen_ThrowsException(GroupReservationStatus status, Type exceptionType, GuestSession guestSession, GroupReservation groupReservation)
    {
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = status;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync(exceptionType, () => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_GuestSessionHasNoDishes_ThrowsCartIsEmptyException(GuestSession guestSession, GroupReservation groupReservation)
    {
        guestSession.Dishes = [];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<CartIsEmptyException>(() => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_GuestSessionSubmitted_ThrowsAlreadySubmited(GuestSession guestSession, GroupReservation groupReservation)
    {
        guestSession.IsSubmitted = true;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GuestSessionSubmittedException>(() => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }

    [Theory]
    [InlineAutoData(DishState.OutOfStock)]
    [InlineAutoData(DishState.OnStop)]
    public async Task SubmitCartAsync_DishNotAvailable_ThrowsDishNotAvailable(DishState state, Dish dish, GuestSession guestSession, GroupReservation groupReservation)
    {
        dish.State = state;
        guestSession.IsSubmitted = false;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        guestSession.Dishes = 
        [
            new OrderItem()
            {
                DishId = dish.Id,
                Quantity = 1,
            }
        ];

        _mockDishRepository
            .Setup(d => d.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([dish]);

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
           .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
           .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<DishIsNotAvailableException>(() => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }
    [Theory]
    [MemberAutoData(nameof(PepareDishesForGuestSession))]
    public async Task GetGuestSessionInfoAsync_Guest_ReturnsAsync(
        IEnumerable<string> dishIds,
        IEnumerable<Dish> dishes,
        List<OrderItem> orderItems,
        decimal expectedTotal,
        GuestSession session)
    {
        session.Dishes = orderItems;
        session.IsOwnerSession = false;

        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(dishIds))
            .ReturnsAsync(dishes);

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(session.Id))
            .ReturnsAsync(session);

        GetGuestSessionResponse result = await _guestSessionService.GetGuestSessionInfoAsync(session.Id);

        Assert.Equal(session.GuestName, result.Name);
        Assert.Equal(dishes.Count(), result.Items.Count());
        Assert.Equal(expectedTotal, result.Total);
    }

    [Theory]
    [MemberAutoData(nameof(PepareDishesForGuestSession))]
    public async Task GetGuestSessionInfoAsync_GroupOwner_ReturnsAsync(
        IEnumerable<string> dishIds,
        IEnumerable<Dish> dishes,
        List<OrderItem> orderItems,
        decimal expectedTotal,
        GuestSession session,
        GroupReservation groupReservation,
        Reservation reservation,
        string customerId,
        string ownerName,
        UserType userType)
    {
        session.Dishes = orderItems;
        session.IsOwnerSession = true;
        session.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = customerId;
        userType.Attributes.Add(new AttributeType()
        {
            Name = "given_name",
            Value = ownerName,
        });

        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(dishIds))
            .ReturnsAsync(dishes);

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(session.Id))
            .ReturnsAsync(session);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _mockUserRepository
            .Setup(r => r.GetUserByIdAsync(customerId))
            .ReturnsAsync(userType);

        GetGuestSessionResponse result = await _guestSessionService.GetGuestSessionInfoAsync(session.Id);

        Assert.Equal(ownerName, result.Name);
        Assert.Equal(dishes.Count(), result.Items.Count());
        Assert.Equal(expectedTotal, result.Total);
    }

    public static TheoryData<IEnumerable<string>, IEnumerable<Dish>, List<OrderItem>, decimal> PepareDishesForGuestSession()
    {
        var dishMap = new Dictionary<string, Dish>()
        {
            ["1"] = new()
            {
                Id = "1",
                Price = 1
            },
            ["2"] = new()
            {
                Id = "2",
                Price = 2
            },
            ["3"] = new()
            {
                Id = "3",
                Price = 3
            }
        };

        var orderItems = new List<OrderItem>
        {
            new OrderItem()
                    {
                        DishId = "1",
                        Quantity = 2
                    },
                    new OrderItem()
                    {
                        DishId = "2",
                        Quantity = 3
                    },
                    new OrderItem()
                    {
                        DishId = "3",
                        Quantity = 1
                    }
        };

        var expectedTotal = orderItems.Sum(o => dishMap[o.DishId].Price * o.Quantity);

        return new()
        {
            { dishMap.Keys, dishMap.Values, orderItems, expectedTotal }
        };
    }

    [Fact]
    public async Task GetGroupSessionsAsync_Owner_ReturnsParticipantsTotalsAndTypes()
    {
        const string userId = "owner-1";
        var group = OpenGroup("group-1", "reservation-1");
        var reservation = OwnedReservation(group, userId);
        SetupOwner(group, reservation, "Ivan");

        var ownerSession = Session("owner-session", group.GroupReservationId, "Owner", true, true,
            Item("dish1", 1));
        var guestSession = Session("session1", group.GroupReservationId, "Andrii", false, false,
            Item("dish1", 2));

        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([ownerSession, guestSession]);
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([Dish("dish1", "Caesar Salad", 180)]);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, group.GroupReservationId);

        Assert.Equal(540, result.Total);
        var owner = Assert.Single(result.Participants, p => p.Type == ParticipantType.Owner);
        Assert.Equal("Ivan", owner.Name);
        Assert.True(owner.IsSubmitted);
        Assert.Equal(180, owner.Total);
        var guest = Assert.Single(result.Participants, p => p.Id == "session1");
        Assert.Equal(ParticipantType.Guest, guest.Type);
        Assert.Equal("Andrii", guest.Name);
        Assert.False(guest.IsSubmitted);
        Assert.Equal(360, guest.Total);
        var item = Assert.Single(guest.Items);
        Assert.Equal("dish1", item.Dish.Id);
        Assert.Equal("Caesar Salad", item.Dish.Name);
        Assert.Equal(180, item.Dish.Price);
        Assert.Equal(2, item.Quantity);
    }

    [Fact]
    public async Task GetGroupSessionsAsync_NonOwner_ThrowsActionForbidden()
    {
        var group = OpenGroup("group-1", "reservation-1");
        var reservation = OwnedReservation(group, "owner-1");
        SetupOwner(group, reservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.GetGroupSessionsAsync("someone-else", group.GroupReservationId));
    }

    [Fact]
    public async Task GetGroupSessionsAsync_MissingGroup_ThrowsGroupReservationNotFound()
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync("missing"))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _guestSessionService.GetGroupSessionsAsync("owner-1", "missing"));
    }

    [Fact]
    public async Task GetOwnerGuestSessionIdAsync_Owner_ReturnsOwnerSessionId()
    {
        const string userId = "owner-1";
        var reservation = OwnedReservation(OpenGroup("group-1", "reservation-1"), userId);
        var group = OpenGroup("group-1", reservation.Id);
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _mockGroupReservationRepository
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(group);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([
                Session("guest-session", group.GroupReservationId, "Olena", false, false),
                Session("owner-session-123", group.GroupReservationId, "Owner", true, false)
            ]);

        var result = await _guestSessionService.GetOwnerGuestSessionIdAsync(userId, reservation.Id);

        Assert.Equal("owner-session-123", result.GuestSessionId);
    }

    [Fact]
    public async Task GetOwnerGuestSessionIdAsync_MissingReservation_ThrowsReservationNotFound()
    {
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync("missing"))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() =>
            _guestSessionService.GetOwnerGuestSessionIdAsync("owner-1", "missing"));
    }

    [Fact]
    public async Task GetOwnerGuestSessionIdAsync_MissingGroup_ThrowsGroupReservationNotFound()
    {
        var reservation = OwnedReservation(OpenGroup("group-1", "reservation-1"), "owner-1");
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _mockGroupReservationRepository
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _guestSessionService.GetOwnerGuestSessionIdAsync("owner-1", reservation.Id));
    }

    [Fact]
    public async Task GetOwnerGuestSessionIdAsync_MissingOwnerSession_ThrowsGuestSessionNotFound()
    {
        var group = OpenGroup("group-1", "reservation-1");
        var reservation = OwnedReservation(group, "owner-1");
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _mockGroupReservationRepository
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(group);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([Session("guest-session", group.GroupReservationId, "Olena", false, false)]);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.GetOwnerGuestSessionIdAsync("owner-1", reservation.Id));
    }

    [Fact]
    public async Task GetOwnerGuestSessionIdAsync_NonOwner_ThrowsActionForbidden()
    {
        var reservation = OwnedReservation(OpenGroup("group-1", "reservation-1"), "owner-1");
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.GetOwnerGuestSessionIdAsync("someone-else", reservation.Id));
    }

    [Fact]
    public async Task SubmitConsolidatedPreOrderAsync_MergesSubmittedDishesAndUpdatesStatuses()
    {
        const string userId = "owner-1";
        var group = OpenGroup("group-1", "reservation-1");
        var reservation = OwnedReservation(group, userId);
        SetupOwner(group, reservation);

        var submittedOwner = Session("owner-session", group.GroupReservationId, "Owner", true, true,
            Item("dish1", 1), Item("dish2", 2));
        var submittedGuest = Session("session1", group.GroupReservationId, "Andrii", false, true,
            Item("dish1", 2));
        var unsubmitted = Session("session2", group.GroupReservationId, "Olena", false, false,
            Item("dish2", 5));

        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([submittedOwner, submittedGuest, unsubmitted]);

        var order = new Order
        {
            OrderId = "order-1",
            CustomerId = userId,
            ReservationId = reservation.Id,
            ReservationAddress = "Main, Table 1",
            ReservationTime = "Oct 31, 2026, 7:00 p.m.",
            Dishes = [],
            Status = OrderStatus.Created
        };
        _mockOrderRepository
            .Setup(r => r.GetOrderByReservationIdAsync(reservation.Id))
            .ReturnsAsync(order);

        var result = await _guestSessionService.SubmitConsolidatedPreOrderAsync(userId, group.GroupReservationId, reservation.Id);

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result.Single(d => d.DishId == "dish1").Quantity);
        Assert.Equal(2, result.Single(d => d.DishId == "dish2").Quantity);
        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
        Assert.Equal(GroupReservationStatus.Submitted, group.Status);
        Assert.Equal(3, order.Dishes.Single(d => d.DishId == "dish1").Quantity);
        Assert.DoesNotContain(order.Dishes, d => d.DishId == "dish2" && d.Quantity == 5);
        _mockOrderRepository.Verify(r => r.SaveOrderAsync(order), Times.Once);
        _mockGroupReservationRepository.Verify(r => r.SaveGroupReservationAsync(group), Times.Once);
    }

    [Fact]
    public async Task SubmitConsolidatedPreOrderAsync_NoSubmittedSessions_ThrowsValidation()
    {
        var group = OpenGroup("group-1", "reservation-1");
        var reservation = OwnedReservation(group, "owner-1");
        SetupOwner(group, reservation);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([Session("session2", group.GroupReservationId, "Olena", false, false, Item("dish2", 1))]);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.SubmitConsolidatedPreOrderAsync("owner-1", group.GroupReservationId, reservation.Id));

        Assert.Equal("No submitted guest sessions found.", exception.Message);
    }

    [Fact]
    public async Task SubmitConsolidatedPreOrderAsync_SubmittedSessionsWithoutDishes_ThrowsValidation()
    {
        var group = OpenGroup("group-1", "reservation-1");
        var reservation = OwnedReservation(group, "owner-1");
        SetupOwner(group, reservation);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([Session("session1", group.GroupReservationId, "Andrii", false, true)]);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.SubmitConsolidatedPreOrderAsync("owner-1", group.GroupReservationId, reservation.Id));

        Assert.Equal("Submitted guest sessions do not contain any dishes.", exception.Message);
    }

    [Fact]
    public async Task SubmitConsolidatedPreOrderAsync_OrderNotFound_ThrowsValidation()
    {
        var group = OpenGroup("group-1", "reservation-1");
        var reservation = OwnedReservation(group, "owner-1");
        SetupOwner(group, reservation);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([Session("session1", group.GroupReservationId, "Andrii", false, true, Item("dish1", 1))]);
        _mockOrderRepository
            .Setup(r => r.GetOrderByReservationIdAsync(reservation.Id))
            .ReturnsAsync((Order?)null);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.SubmitConsolidatedPreOrderAsync("owner-1", group.GroupReservationId, reservation.Id));

        Assert.Equal("Order not found.", exception.Message);
    }

    [Fact]
    public async Task SubmitConsolidatedPreOrderAsync_ReservationDoesNotBelongToGroup_ThrowsValidation()
    {
        var group = OpenGroup("group-1", "reservation-1");
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(group.GroupReservationId))
            .ReturnsAsync(group);
        var otherReservation = OwnedReservation(OpenGroup("group-2", "reservation-2"), "owner-1");
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(otherReservation.Id))
            .ReturnsAsync(otherReservation);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.SubmitConsolidatedPreOrderAsync("owner-1", group.GroupReservationId, otherReservation.Id));

        Assert.Equal("Reservation does not belong to this group reservation.", exception.Message);
    }

    [Fact]
    public async Task SubmitConsolidatedPreOrderAsync_MissingReservation_ThrowsReservationNotFound()
    {
        var group = OpenGroup("group-1", "reservation-1");
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(group.GroupReservationId))
            .ReturnsAsync(group);
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync("missing"))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() =>
            _guestSessionService.SubmitConsolidatedPreOrderAsync("owner-1", group.GroupReservationId, "missing"));
    }

    [Fact]
    public async Task SubmitConsolidatedPreOrderAsync_MissingGroup_ThrowsGroupReservationNotFound()
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync("missing"))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _guestSessionService.SubmitConsolidatedPreOrderAsync("owner-1", "missing", "reservation-1"));
    }

    [Fact]
    public async Task SubmitConsolidatedPreOrderAsync_NonOwner_ThrowsActionForbidden()
    {
        var group = OpenGroup("group-1", "reservation-1");
        var reservation = OwnedReservation(group, "owner-1");
        SetupOwner(group, reservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.SubmitConsolidatedPreOrderAsync("someone-else", group.GroupReservationId, reservation.Id));
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_ReturnsRemainingSnapshotAndRecalculatedTotal()
    {
        var group = OpenGroup("group-1", "reservation-1");
        var reservation = OwnedReservation(group, "owner-1");
        SetupOwner(group, reservation);
        var removed = Session("session1", group.GroupReservationId, "Andrii", false, true, Item("dish1", 2));
        var remaining = Session("session2", group.GroupReservationId, "Olena", false, false, Item("dish2", 1));
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(removed.Id))
            .ReturnsAsync(removed);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([remaining]);
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([Dish("dish2", "Tom Yum", 220)]);

        var result = await _guestSessionService.DeleteGuestSessionAsync("owner-1", group.GroupReservationId, removed.Id);

        var participant = Assert.Single(result.Participants);
        Assert.Equal("session2", participant.Id);
        Assert.Equal(220, participant.Total);
        Assert.Equal(220, result.Total);
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(removed), Times.Once);
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_LastSession_ReturnsEmptySnapshot()
    {
        var group = OpenGroup("group-1", "reservation-1");
        var reservation = OwnedReservation(group, "owner-1");
        SetupOwner(group, reservation);
        var removed = Session("session1", group.GroupReservationId, "Andrii", false, true);
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(removed.Id))
            .ReturnsAsync(removed);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(group.GroupReservationId))
            .ReturnsAsync([]);

        var result = await _guestSessionService.DeleteGuestSessionAsync("owner-1", group.GroupReservationId, removed.Id);

        Assert.Empty(result.Participants);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_SessionBelongsToAnotherGroup_ThrowsValidation()
    {
        var group = OpenGroup("group-1", "reservation-1");
        var reservation = OwnedReservation(group, "owner-1");
        SetupOwner(group, reservation);
        var session = Session("session1", "other-group", "Andrii", false, false);
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(session.Id))
            .ReturnsAsync(session);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.DeleteGuestSessionAsync("owner-1", group.GroupReservationId, session.Id));

        Assert.Equal("Guest session does not belong to this group reservation.", exception.Message);
    }

    [Fact]
    public async Task DeleteGuestSessionAsync_SessionNotFound_ThrowsGuestSessionNotFound()
    {
        var group = OpenGroup("group-1", "reservation-1");
        var reservation = OwnedReservation(group, "owner-1");
        SetupOwner(group, reservation);
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId("missing"))
            .ReturnsAsync((GuestSession?)null);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.DeleteGuestSessionAsync("owner-1", group.GroupReservationId, "missing"));
    }

    private void SetupOwner(GroupReservation group, Reservation reservation, string? ownerName = null)
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(group.GroupReservationId))
            .ReturnsAsync(group);
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        if (ownerName is null)
            return;

        _mockUserRepository
            .Setup(r => r.GetUserByIdAsync(reservation.CustomerId!))
            .ReturnsAsync(new UserType
            {
                Attributes =
                [
                    new AttributeType { Name = "given_name", Value = ownerName }
                ]
            });
    }

    private static GroupReservation OpenGroup(string groupReservationId, string reservationId) => new()
    {
        GroupReservationId = groupReservationId,
        ReservationId = reservationId,
        ReservationToken = "token",
        Status = GroupReservationStatus.Open
    };

    private static Reservation OwnedReservation(GroupReservation group, string userId) => new()
    {
        Id = group.ReservationId,
        TableId = "table-1",
        WaiterId = "waiter-1",
        CustomerId = userId
    };

    private static GuestSession Session(
        string id,
        string groupReservationId,
        string name,
        bool isOwner,
        bool isSubmitted,
        params OrderItem[] dishes) => new()
    {
        Id = id,
        GroupReservationId = groupReservationId,
        GuestName = name,
        IsOwnerSession = isOwner,
        IsSubmitted = isSubmitted,
        Dishes = dishes.ToList()
    };

    private static OrderItem Item(string dishId, int quantity) => new()
    {
        DishId = dishId,
        Quantity = quantity
    };

    private static Dish Dish(string id, string name, decimal price) => new()
    {
        Id = id,
        Name = name,
        Price = price
    };
}





