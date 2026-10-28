// edge i of a pointy-top hex lies between corner i and i+1; neighbour in that direction (odd-r rows):
export const EDGE_NEIGHBOR_EVEN: Array<[number, number]> = [[1, 0], [0, 1], [-1, 1], [-1, 0], [-1, -1], [0, -1]];
export const EDGE_NEIGHBOR_ODD: Array<[number, number]> = [[1, 0], [1, 1], [0, 1], [-1, 0], [0, -1], [1, -1]];
