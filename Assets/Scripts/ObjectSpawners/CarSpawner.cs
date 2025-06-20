using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CarSpawner : MonoBehaviour
{

    public GameObject[] Cars;
    void Start()
    {
        Invoke("SpawnCar", 0.5f);
    }

    void Update()
    {

    }

    void SpawnCar()
    {
        float carSpawnInterval = Random.Range(3, 10.0f);
        int carNumber = Random.Range(0, Cars.Length);

        int carDirection = Random.Range(0, 2);
        if (carDirection == 1)
        {
            Instantiate(Cars[carNumber], new Vector3(17f, -0.25f, -60f), transform.rotation);
        }
        else if (carDirection == 0)
        {
            Instantiate(Cars[carNumber], new Vector3(12f, -0.25f, 24f), Quaternion.Euler(0, 180, 0));
        }


        Invoke("SpawnCar", carSpawnInterval);
    }

}
