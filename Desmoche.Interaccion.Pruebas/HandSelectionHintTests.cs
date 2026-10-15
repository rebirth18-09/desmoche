using System;
using System.Linq;
using Desmoche.Domain;
using Desmoche.Presentation.Interaction;

namespace Desmoche.Interaccion.Pruebas;

internal static class HandSelectionHintTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool valid, string label)
        { if (!valid) throw new InvalidOperationException(label); checks++; }
        foreach (int[] group in new[] { new[] { 0, 13, 26 }, new[] { 0, 13, 26, 39 },
            new[] { 0, 1, 2 }, new[] { 10, 11, 12, 0 }, new[] { 12, 11, 0 } })
        {
            foreach (int[] order in Permutations(group))
            {
                int[] before = order.ToArray();
                Check(HandSelectionHint.FormsMeld(order), "Un juego posible se reconoce en cualquier orden.");
                Check(order.SequenceEqual(before), "El indicador no cambia el orden de las cartas.");
            }
        }
        foreach (int[] selection in new[] { Array.Empty<int>(), new[] { 0 }, new[] { 0, 1 },
            new[] { 0, 2, 3 }, new[] { 0, 1, 15 }, new[] { 12, 0, 1 }, new[] { 0, 0, 1 },
            new[] { -1, 0, 1 }, new[] { 52, 0, 1 }, new[] { 0, 13, 26, 1, 14, 27 },
            new[] { 0, 1, 2, 39 }, new[] { 0, 13, 26, 39, 1 } })
            Check(!HandSelectionHint.FormsMeld(selection), "Una selección incompleta, ajena, duplicada o con sobrantes no se marca verde.");
        int[] shuffled = { 2, 0, 1 };
        Check(HandSelectionHint.FormsMeld(shuffled) && !RulesValidator.ValidateMeld(shuffled.Select(Card.FromId).ToArray()).IsValid,
            "El indicio de posibilidad no cambia la exigencia de orden al confirmar.");
        Check(HandSelectionHint.FormsMeld(Enumerable.Range(0, 13).Reverse().ToArray()), "Escalera completa de 13.");
        foreach (int[] pair in new[] { new[] { 2, 0 }, new[] { 0, 2 } })
        {
            Check(HandSelectionHint.FormsMeld(pair, 1), "Dos de mano y oferta forman escalera sin exigir orden.");
            Check(!HandSelectionHint.FormsMeld(pair, null), "La oferta no elegida no completa el indicador.");
        }
        Check(HandSelectionHint.FormsMeld(new[] { 13, 0 }, 26), "Oferta completa tercia.");
        Check(HandSelectionHint.FormsMeld(new[] { 26, 0, 13 }, 39), "Oferta completa cuarta.");
        Check(HandSelectionHint.FormsMeld(new[] { 12, 11 }, 0), "Oferta completa escalera con As alto.");
        Check(!HandSelectionHint.FormsMeld(new[] { 0 }, 1), "Una carta propia y una externa no forman juego.");
        Check(!HandSelectionHint.FormsMeld(new[] { 0, 4 }, 1), "La oferta no completa un juego incompatible.");
        Check(!HandSelectionHint.FormsMeld(new[] { 0, 1 }, 0), "La oferta no puede duplicar una carta.");
        Check(!HandSelectionHint.FormsMeld(new[] { 0, 2 }, 52), "Oferta de identidad inválida.");
        Console.WriteLine($"INDICADOR DE SELECCION: {checks} comprobaciones correctas.");
        return checks;
    }

    private static System.Collections.Generic.IEnumerable<int[]> Permutations(int[] cards)
    {
        if (cards.Length == 0) { yield return Array.Empty<int>(); yield break; }
        for (int i = 0; i < cards.Length; i++)
            foreach (int[] tail in Permutations(cards.Where((_, index) => index != i).ToArray()))
                yield return new[] { cards[i] }.Concat(tail).ToArray();
    }
}
