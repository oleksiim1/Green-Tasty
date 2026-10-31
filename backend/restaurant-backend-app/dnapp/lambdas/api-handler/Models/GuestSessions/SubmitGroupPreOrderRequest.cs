using System.ComponentModel.DataAnnotations;

namespace APIHandler.Models.GuestSessions;

public record SubmitGroupPreOrderRequest([property: Required] string ReservationId);
