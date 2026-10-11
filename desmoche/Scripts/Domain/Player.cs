using System;

namespace Desmoche.Domain;

public sealed class Player
{
	public int Id { get; }
	public string Name { get; }
	public int SeatIndex { get; }
	public PlayerHand Hand { get; }

	public Player(int id, string name, int seatIndex)
		: this(id, name, seatIndex, new PlayerHand())
	{
	}

	private Player(
		int id,
		string name,
		int seatIndex,
		PlayerHand hand)
	{
		if (id <= 0)
			throw new ArgumentOutOfRangeException(nameof(id));

		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException(
				"El nombre es obligatorio.",
				nameof(name));
		}

		if (seatIndex < 0 || seatIndex > 3)
			throw new ArgumentOutOfRangeException(nameof(seatIndex));

		Id = id;
		Name = name.Trim();
		SeatIndex = seatIndex;
		Hand = hand;
	}

	internal Player WithHand(PlayerHand hand)
	{
		ArgumentNullException.ThrowIfNull(hand);

		return new Player(Id, Name, SeatIndex, hand);
	}
}
