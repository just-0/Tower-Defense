import cv2
import numpy as np

class ArucoDetector:
    def __init__(self, 
                 aruco_dict_type=cv2.aruco.DICT_4X4_50,
                 marker_length=0.05,
                 camera_matrix=None,
                 dist_coeffs=None):
        self.aruco_dict = cv2.aruco.getPredefinedDictionary(aruco_dict_type)
        self.parameters = cv2.aruco.DetectorParameters()
        self.marker_length = marker_length
        self.camera_matrix = camera_matrix
        self.dist_coeffs = dist_coeffs
        self._set_parameters()

    def _set_parameters(self):
        p = self.parameters
        p.adaptiveThreshWinSizeMin = 3
        p.adaptiveThreshWinSizeMax = 25
        p.adaptiveThreshWinSizeStep = 8
        p.adaptiveThreshConstant = 7
        p.minMarkerPerimeterRate = 0.02
        p.maxMarkerPerimeterRate = 4.0
        p.polygonalApproxAccuracyRate = 0.03
        p.minCornerDistanceRate = 0.03
        p.minDistanceToBorder = 1
        p.minMarkerDistanceRate = 0.03
        p.cornerRefinementMethod = cv2.aruco.CORNER_REFINE_SUBPIX
        p.cornerRefinementWinSize = 3
        p.cornerRefinementMaxIterations = 15
        p.cornerRefinementMinAccuracy = 0.05
        p.markerBorderBits = 1
        p.perspectiveRemovePixelPerCell = 4
        p.perspectiveRemoveIgnoredMarginPerCell = 0.1
        p.maxErroneousBitsInBorderRate = 0.4
        p.minOtsuStdDev = 3.0
        p.errorCorrectionRate = 0.7

    def detect(self, frame, draw=True, upscale_if_not_found=True):
        """
        Detecta marcadores ArUco en un frame dado.
        Args:
            frame: imagen BGR de entrada
            draw: si True, dibuja los marcadores detectados en el frame
            upscale_if_not_found: si True, reintenta con imagen ampliada si no detecta nada
        Returns:
            ids: lista de IDs detectados (o None)
            centers: lista de (x, y) de cada marcador (o None)
            corners: lista de coordenadas de los vértices de cada marcador (o None)
            frame_out: frame con marcadores dibujados (si draw=True)
        """
        frame_out = frame.copy()
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        kernel = np.array([[-1,-1,-1], [-1,9,-1], [-1,-1,-1]])
        gray = cv2.filter2D(gray, -1, kernel)
        corners, ids, _ = cv2.aruco.detectMarkers(gray, self.aruco_dict, parameters=self.parameters)

        # Si no encuentra, reintenta con imagen ampliada
        if (ids is None or len(ids) == 0) and upscale_if_not_found:
            small_gray = cv2.resize(gray, None, fx=1.5, fy=1.5, interpolation=cv2.INTER_CUBIC)
            corners_small, ids_small, _ = cv2.aruco.detectMarkers(small_gray, self.aruco_dict, parameters=self.parameters)
            if ids_small is not None and len(ids_small) > 0:
                corners = []
                ids = ids_small
                for corner in corners_small:
                    corners.append(corner / 1.5)
                corners = tuple(corners)

        centers = []
        if ids is not None and len(ids) > 0:
            if draw:
                cv2.aruco.drawDetectedMarkers(frame_out, corners, ids)
            for i, corner in enumerate(corners):
                center_x = int(np.mean(corner[0][:, 0]))
                center_y = int(np.mean(corner[0][:, 1]))
                centers.append((center_x, center_y))
        else:
            ids = None
            centers = None
            corners = None

        return ids, centers, corners, frame_out

# Ejemplo de uso (no ejecutar aquí, solo referencia):
# detector = ArucoDetector()
# ids, centers, corners, frame_out = detector.detect(frame)
    