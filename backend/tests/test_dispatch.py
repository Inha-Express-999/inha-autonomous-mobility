from __future__ import annotations

import unittest

from campus_sim.dispatch import minimum_cost_assignment


class MinimumCostAssignmentTests(unittest.TestCase):
    def test_selects_global_minimum_instead_of_row_greedy(self) -> None:
        costs = [[1.0, 2.0], [1.1, 100.0]]

        self.assertEqual(minimum_cost_assignment(costs), [(0, 1), (1, 0)])

    def test_maximizes_feasible_matches_and_skips_forbidden_pairs(self) -> None:
        costs = [[0.5, None], [0.0, None], [None, 1.0]]

        assignments = minimum_cost_assignment(costs)

        self.assertEqual(len(assignments), 2)
        self.assertTrue(all(costs[row][column] is not None for row, column in assignments))
        self.assertEqual(len({row for row, _ in assignments}), 2)
        self.assertEqual(len({column for _, column in assignments}), 2)

    def test_returns_empty_for_empty_or_entirely_infeasible_matrix(self) -> None:
        self.assertEqual(minimum_cost_assignment([]), [])
        self.assertEqual(minimum_cost_assignment([[None], [None]]), [])

    def test_large_finite_costs_do_not_overflow_dummy_costs(self) -> None:
        self.assertEqual(minimum_cost_assignment([[1e308, 1e308]]), [(0, 0)])

    def test_rejects_ragged_or_invalid_cost_matrices(self) -> None:
        with self.assertRaisesRegex(ValueError, "rectangular"):
            minimum_cost_assignment([[1.0], [1.0, 2.0]])
        with self.assertRaisesRegex(ValueError, "finite and non-negative"):
            minimum_cost_assignment([[-1.0]])
        with self.assertRaisesRegex(ValueError, "finite and non-negative"):
            minimum_cost_assignment([[float("nan")]])


if __name__ == "__main__":
    unittest.main()
