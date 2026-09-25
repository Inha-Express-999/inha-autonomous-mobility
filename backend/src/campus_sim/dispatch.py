from __future__ import annotations

import math
from collections.abc import Sequence


def minimum_cost_assignment(
    costs: Sequence[Sequence[float | None]],
) -> list[tuple[int, int]]:
    """Return a maximum-cardinality, minimum-cost partial row/column matching.

    ``None`` marks an infeasible pair. Rows and columns are zero-indexed in the
    returned pairs. Ties are resolved by input row/column order.
    """
    if not costs:
        return []
    column_count = len(costs[0])
    if column_count == 0 or any(len(row) != column_count for row in costs):
        raise ValueError("cost matrix must be rectangular and have at least one column")

    feasible_costs = [cost for row in costs for cost in row if cost is not None]
    for cost in feasible_costs:
        if not math.isfinite(cost) or cost < 0:
            raise ValueError("feasible costs must be finite and non-negative")
    if not feasible_costs:
        return []

    row_count = len(costs)
    maximum_cost = max(feasible_costs)
    cost_scale = max(1.0, maximum_cost)
    unmatched_cost = float(row_count + 1)
    forbidden_cost = float((row_count + 1) ** 2)
    # One dummy column per row permits partial matching. Its penalty dominates
    # every possible feasible total, so the solver first maximizes match count.
    matrix = [
        [forbidden_cost if cost is None else cost / cost_scale for cost in row]
        + [unmatched_cost] * row_count
        for row in costs
    ]

    # Hungarian algorithm for n <= m, using 1-based potentials.
    n = row_count
    m = column_count + row_count
    row_potential = [0.0] * (n + 1)
    column_potential = [0.0] * (m + 1)
    matched_row = [0] * (m + 1)
    previous_column = [0] * (m + 1)

    for row_index in range(1, n + 1):
        matched_row[0] = row_index
        current_column = 0
        minimum_slack = [math.inf] * (m + 1)
        visited = [False] * (m + 1)
        while True:
            visited[current_column] = True
            current_row = matched_row[current_column]
            delta = math.inf
            next_column = 0
            for column_index in range(1, m + 1):
                if visited[column_index]:
                    continue
                reduced_cost = (
                    matrix[current_row - 1][column_index - 1]
                    - row_potential[current_row]
                    - column_potential[column_index]
                )
                if reduced_cost < minimum_slack[column_index]:
                    minimum_slack[column_index] = reduced_cost
                    previous_column[column_index] = current_column
                if minimum_slack[column_index] < delta:
                    delta = minimum_slack[column_index]
                    next_column = column_index
            for column_index in range(m + 1):
                if visited[column_index]:
                    row_potential[matched_row[column_index]] += delta
                    column_potential[column_index] -= delta
                else:
                    minimum_slack[column_index] -= delta
            current_column = next_column
            if matched_row[current_column] == 0:
                break
        while True:
            prior = previous_column[current_column]
            matched_row[current_column] = matched_row[prior]
            current_column = prior
            if current_column == 0:
                break

    assignments: list[tuple[int, int]] = []
    for column_index in range(1, column_count + 1):
        row_index = matched_row[column_index]
        if row_index and costs[row_index - 1][column_index - 1] is not None:
            assignments.append((row_index - 1, column_index - 1))
    return sorted(assignments)
